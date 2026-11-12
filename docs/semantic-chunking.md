# Semantic Chunking

The `--strategy semantic` strategy splits text at semantic boundaries rather
than only at punctuation: neighboring sentences are compared with embeddings,
and a chunk ends where the topic changes.

## How It Works

```
text ──► SemanticAtomizer ──► atoms (sentences, paragraphs, headings, code, lists)
                                         │
                                         ▼
                           embeddings (llama.cpp / hash) + cache
                                         │
                                         ▼
                     d[i] = 1 − cos (optional TextTiling window)
                                         │
                      breakpoints = percentile(d, p) ∧ d ≥ minDistance
                                         │
                      SemanticChunker: grouping + limits + overlap
```

1. **Atomization.** Markdown headings, list items and code blocks become
   separate atoms (code is never split). The remaining text is split into
   sentences with protection for abbreviations (`т. д.`, `см.`, `стр.`,
   `Mr.`, initials) and decimal fractions. Atoms that are too long
   (> `--semantic-max-atom-tokens`) are split in advance; short ones are
   merged with their neighbors, with a short sub-heading at the start of a
   paragraph pulling the following sentence with it rather than being glued
   to the previous topic.
2. **Embeddings.** Atoms are sent to the server in batches; identical texts
   are deduplicated and the results are cached on disk.
3. **Boundaries.** The distance between neighboring atoms is `1 − cosine`.
   A boundary is set where the distance is at least the percentile `p` and
   not below `minDistance`. With `--semantic-window N`, the mean vectors of
   the left and right windows are compared (TextTiling), which is more robust
   to noise. Markdown headings always start a new chunk — this is a structural
   boundary, independent of embeddings (a short heading may be close to
   neighboring text).
4. **Grouping.** Atoms accumulate up to a semantic boundary or up to the
   limit (`--max-tokens` / `--max-chars` / …, taking `--prefix` into account).
   If an atom does not fit on its own — a hard cut, aligned on whitespace, so
   that words are not split. Overlap is accumulated from whole atoms.
5. **Streaming for large files.** With `--semantic-fallback streaming`, the
   document is read in windows of `--semantic-stream-window-bytes` with an
   overlap of `--semantic-stream-overlap-bytes`: each window is planned
   separately, and a chunk that starts inside the overlap is cut off at the
   previous window's boundary. Memory is bounded by the window, not by the
   file size. `--min-*`/`--drop-small` are not applied in streaming mode,
   so that text is not lost at window borders.

## Starting the Embedding Server

The application does not start the server itself — it only calls it over HTTP.
A verified option with `llama.cpp` and `embeddinggemma-2`:

```bat
D:\opt\llama\llama-server.exe ^
  -m D:\models\embeddinggemma-2\embeddinggemma-2-BF16.gguf ^
  --embedding --pooling mean --embd-normalize 2 ^
  -a embeddinggemma-2 ^
  -c 8192 --parallel 4 ^
  -ngl 99 --host 127.0.0.1 --port 9932 --no-webui
```

- `--embedding` is required; `--pooling mean` and `--embd-normalize 2`
  match the model (`1_Pooling/config.json` → mean, `2_Normalize`).
- `-a embeddinggemma-2` gives a clean id for `/v1/models` and the `model`
  field.
- `--parallel N` sets the number of slots; take `--embedding-concurrency N`
  no higher than N.
- `--mmproj` is only needed for images; for text it is unnecessary and
  eats memory.
- Sampling parameters (`--temperature`, `--top-p`, …) do not affect
  embeddings.

Verification:

```bash
curl http://127.0.0.1:9932/health
curl http://127.0.0.1:9932/v1/embeddings \
  -H "Content-Type: application/json" \
  -d '{"model":"embeddinggemma-2","input":["hello world"]}'
```

A 768-dimensional L2-normalized vector is expected. The first request after a
restart may take up to ~10 s (CUDA warmup); subsequent ones take tens of
milliseconds.

## Usage

```bash
# local llama.cpp + exact token counting
textchunker --input document.md \
  --strategy semantic \
  --embedding-provider llama-cpp \
  --embedding-endpoint http://127.0.0.1:9932 \
  --embedding-model embeddinggemma-2 \
  --tokenizer huggingface --tokenizer-path ./tokenizer.json \
  --max-tokens 512 \
  --prefix "title: none | text: " \
  --stats
```

Offline mode without a model (deterministic hashing trick, for pipeline
checks and tests):

```bash
textchunker --input document.md --strategy semantic \
  --embedding-provider hash --no-embedding-cache --max-tokens 512
```

## Parameters

| Option | Default | Purpose |
|---|---|---|
| `--embedding-provider` | `llama-cpp` | `llama-cpp` \| `hash` |
| `--embedding-endpoint` | `http://127.0.0.1:9932` | base URL of the server |
| `--embedding-model` | — | value of the `model` field in the request |
| `--embedding-batch-size` | `32` | atoms per single HTTP request |
| `--embedding-max-batch-chars` | `8000` | character budget of a batch (conservative token estimate; `0` — no limit) |
| `--embedding-dimensions` | `768` | expected vector dimension (`0` — do not check) |
| `--embedding-concurrency` | `1` | parallel batches (the server must have `--parallel N`) |
| `--embedding-timeout` | `120` | HTTP request timeout, seconds |
| `--embedding-max-retries` | `3` | retries for network, 429 and 5xx |
| `--embedding-prompt` | — | prefix appended to each atom (part of the cache key) |
| `--embedding-cache-dir` | `%LOCALAPPDATA%\TextChunker\embeddings` | cache directory |
| `--embedding-cache-max-bytes` | — | cache size limit (pre-run auto-cleanup) |
| `--embedding-cache-ttl-days` | — | delete entries older than N days (pre-run auto-cleanup) |
| `--no-embedding-cache` | — | disable the cache |
| `--semantic-percentile` | `0.90` | percentile of distances for boundaries |
| `--semantic-min-distance` | `0.02` | minimum boundary distance |
| `--semantic-window` | `1` | TextTiling window (`1` — neighboring atoms) |
| `--semantic-max-bytes` | `20000000` | input limit for `recursive`/`error` (20 MB) |
| `--semantic-fallback` | `recursive` | `recursive` — fallback, `error` — error, `streaming` — windows |
| `--semantic-stream-window-bytes` | `4000000` | size of a streaming-mode window, bytes |
| `--semantic-stream-overlap-bytes` | `256000` | overlap between neighboring windows |
| `--semantic-min-atom-chars` | `20` | merging of short atoms |
| `--semantic-max-atom-tokens` | `512` | pre-splitting of long atoms |

The API key (if the server is locked behind `--api-key`) is read **only** from
the environment variable `TEXTCHUNKER_EMBEDDING_API_KEY` and is sent as
`Authorization: Bearer`.

## Quality Tuning

- **Too few boundaries** → decrease `--semantic-percentile` (e.g. 0.8).
- **Too many boundaries** → increase the percentile (0.95) and/or
  `--semantic-min-distance`.
- **Absolute thresholds do not work:** for embeddinggemma, unrelated texts
  have cosine 0.65–0.72, related ones — 0.95+. Comparisons are always
  relative (percentile).
- `--embedding-prompt "title: none | text: "` is applied equally to all
  atoms. Usually not needed for boundary detection; enable it if atom
  embeddings must be compatible with a retrieval prefix.

## Calibration

Defaults are verified on two corpora with the `tools/SemanticCalibration`
instrument; the metric is boundary F1 with a tolerance of ±1–2 atoms.

| Corpus | percentile | window | Precision | Recall | F1 |
|---|---:|---:|---:|---:|---:|
| synthetic (10 docs) | 0.90 | 1 | 0.629 | 0.957 | 0.759 |
| synthetic (10 docs) | 0.95 | 1 | 0.720 | 0.783 | 0.750 |
| synthetic, prose (5) | 0.90 | 1 | 0.818 | 0.900 | 0.857 |
| synthetic (10 docs) | 0.90 | 3 | 0.515 | 0.739 | 0.607 |
| Wikipedia (39 docs) | 0.90 | 1 | 0.126 | 0.515 | 0.203 |
| Wikipedia (39 docs) | 0.95 | 3 | 0.207 | 0.426 | 0.279 |
| Wikipedia (39 docs) | 0.95 | 5 | 0.222 | 0.456 | 0.298 |
| Wikipedia (39 docs) | 0.97 | 3 | 0.265 | 0.335 | 0.296 |

- `--semantic-percentile 0.90` gives the best F1 on short/structured texts;
  0.95 is more precise but loses recall.
- `--semantic-window 1` is better than window 3 on the synthetic corpus; for
  long Wikipedia articles, windows 3–5 with a percentile of 0.95–0.97 work
  better.
- Wikipedia sections are editorial structure, not always a thematic shift,
  so F1 is lower there. The `0.90/1` default is kept as a compromise; for
  your own domain run `tools/SemanticCalibration` and look at `--verbose` /
  `--dump-atoms` ([tests/calibration/README.md](../tests/calibration/README.md)).

## Performance

Measured on 2026-11-04: Ryzen 5 5600X, 2×GPU (RTX 5060 Ti 16 GB + GTX 1660
SUPER 6 GB), llama.cpp b12083, `embeddinggemma-2-BF16`, `--parallel 4`,
batch 32, `--max-tokens 512`.

| Input | Atoms | `--embedding-concurrency` | Time | Peak RSS |
|---|---:|---:|---:|---:|
| 1 MB unique text | 5 177 | 1 | 12.1 s | 113 MB |
| 1 MB unique text | 5 177 | 4 | 8.3 s | 115 MB |
| 5 MB unique text | 25 685 | 4 | 61.2 s | 342 MB |
| 20 MB, streaming 4 MB / 256 KB | 108 712 | 4 | 221.5 s | 516 MB |
| 1 MB, cold cache | 5 177 | 4 | 18.2 s | 114 MB |
| 1 MB, warm cache | 5 177 | 4 | 2.0 s | 62 MB |

- `--embedding-concurrency 4` gives ~1.45× on 1 MB; further gains are
  limited by the GPU.
- Cold cache write adds ~10 s; a warm run — 2 s (5 177 hits).
- Document-mode: linearly ~12 s/MB, 20 MB ≈ 4 minutes and ~1.3 GB RSS
  (extrapolated).
- Streaming 20 MB (window 4 MB, overlap 256 KB): **3 min 42 s, peak 516
  MB**; 11 567 chunks, no gaps and no duplicates. Memory is bounded by the
  window.
- Streaming check: 2.65 MB of Wikipedia articles, window 256 KB, overlap
  32 KB, concurrency 4 — 81.8 s, 2001 chunks, no gaps and no duplicates;
  memory is bounded by the window. For comparison, the same file with `hash`
  — 0.9 s (no network).

That is why semantic works in document-mode with a `--semantic-max-bytes`
limit: all text is buffered, the plan is built, then chunking runs. Larger
files fall back to `recursive` with a warning (or fail with
`--semantic-fallback error`). For files larger than the limit you can enable
`--semantic-fallback streaming`: the document is processed in 4 MB windows
(configurable), and memory is bounded by the window.

The embedding cache (`sha256(model + prompt + text)`) makes repeated runs
almost free: misses — only for new atoms. The cache directory scope includes
the endpoint, model and dimension, so models with the same name on different
servers do not conflict. Identical texts within a single run are
deduplicated, so repeated fragments are not embedded again.

Cache maintenance (CLI, no input read):

```bash
# see what will be deleted
textchunker --prune-cache --prune-cache-dry-run \
  --embedding-cache-ttl-days 30 \
  --embedding-cache-max-bytes 500000000

# delete
textchunker --prune-cache \
  --embedding-cache-ttl-days 30 \
  --embedding-cache-max-bytes 500000000
```

In a regular run, `--embedding-cache-ttl-days` and
`--embedding-cache-max-bytes` enable auto-cleanup before accessing the cache.
The last entry is not deleted even if it exceeds the limit.

## Metadata

Chunks from semantic chunking additionally contain:

```json
"metadata": {
  "strategy": "semantic",
  "semanticProvider": "llama-cpp",
  "semanticModel": "embeddinggemma-2",
  "breakpointPercentile": 0.9,
  "semanticFallback": "recursive"
}
```

`semanticFallback` appears only if the fallback was triggered;
`semanticModel` — if the provider knows the model.

## Stats

With `--stats` (stderr) and in the API `stats`, the diagnostic fields appear:

```json
{"chunkCount":8,"semanticAtomCount":23,"embeddingCacheHits":18,"embeddingCacheMisses":5}
```

- `semanticAtomCount` — how many atoms the atomizer produced (visible how
  finely the text is split);
- `embeddingCacheHits` / `embeddingCacheMisses` — how many atoms were
  served from the cache and how many had to be computed; they help estimate
  the cost of a re-run. The fields are absent when the cache is disabled
  (`--no-embedding-cache`).

## Errors

| Situation | CLI | API |
|---|---:|---:|
| Embedding server unreachable / invalid response | 5 | 503 |
| Unknown provider, invalid percentile, prefix ≥ limit | 2 | 400 |
| Document larger than `--semantic-max-bytes` with `fallback=error` | 2 | 413 |
| Document larger than the limit with `fallback=recursive` | 0 + warning | 200 + `semanticFallback` |
| `fallback=streaming` | 0, 4 MB windows | 200 + `semanticFallback: streaming` |

If llama.cpp returns `input ... too large to process` (the server's physical
batch is smaller than our portion), the provider automatically halves the
batch, then the text, and averages the parts' vectors. If even a short input
does not pass — an error with advice to increase the server's `-b/--batch-size`.

## Tests

- Unit (no network): atomization, breakpoints on scripted vectors, chunker,
  cache, HTTP provider via a fake handler (including parallel batches, 429,
  timeout and adaptive splitting of oversized input), streaming windows.
- CLI/API with `--embedding-provider hash` — offline, including
  `fallback=streaming`.
- Gated integration with a live server:

```bash
TEXTCHUNKER_LLAMA_CPP_URL=http://127.0.0.1:9932 \
TEXTCHUNKER_LLAMA_CPP_MODEL=embeddinggemma-2 \
dotnet test tests/TextChunker.Tests --filter LlamaCppSemanticIntegration
```

- CI: `.github/workflows/semantic-live.yml` — manual/weekly run with
  `llama-server` and a compact 768-dim `nomic-embed-text-v1.5` model
  (CPU, Ubuntu). It is not included in the regular push-CI.
- 429 retries and timeout were verified on a live HTTP through a local proxy:
  2 responses 429 → 5 requests → success; 5 s delay at
  `--embedding-timeout 1` → exit 5 in ~1.1 s.

## Limitations

- Document-mode (default): all text in memory, 20 MB limit.
- `--semantic-fallback streaming` removes the limit, but boundaries at window
  borders may be less accurate; the window and overlap are configurable.
- `semanticAtomCount` in streaming — the sum over windows (including
  overlaps).
- Semantic quality depends on the model and the llama.cpp version; fix the
  quantization and parameters.
- Markdown structure (headings, lists, code) is recognized, but not parsed
  fully.
