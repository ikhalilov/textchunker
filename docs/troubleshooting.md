# Diagnostics and Common Issues

## Cyrillic is displayed as `?`

Cause: the console/debug window uses a code page without Cyrillic support.
The utility:

- when stdout/stderr are redirected, writes UTF-8 without BOM;
- when an interactive console is available, switches
  `Console.OutputEncoding`/`InputEncoding` to UTF-8.

If `?` remains:

1. Enable UTF-8 in the terminal (`chcp 65001` in cmd, encoding settings in
   Windows Terminal / VS / Rider).
2. Choose a Cyrillic-capable font (Consolas, Cascadia Mono).
3. Make sure that third-party wrappers/loggers are not re-encoding the stream
   to cp1251/cp866.

## `stdin not redirected (interactive terminal)`

The utility does not block on an interactive stdin (`Console.IsInputRedirected ==
false`). Options:

```bash
textchunker --input document.txt
cat document.txt | textchunker --input -
```

In the debugger, configure stdin redirection to a file (or use
`--input <file>`).

## Model tokenizer does not load (exit 5)

Check:

- `--tokenizer huggingface` is set and `--tokenizer-model` is correct
  (default `google/embeddinggemma-2`);
- cache: `%LOCALAPPDATA%\TextChunker\tokenizers\<sanitized-model>\tokenizer.json`
  (Linux/macOS — `LocalApplicationData/TextChunker/tokenizers/...`);
- network access on first download (or a local `tokenizer.json` via
  `--tokenizer-path`);
- the file is ~32 MB and is valid JSON.

The error is written to stderr, code — 5. Once the file is in the cache the
tool works offline.

## Invalid input encoding

- Input is UTF-8 by default; the BOM is stripped.
- `--encoding windows-1251` (and other code pages) — for **file** input.
- `--invalid-encoding error` — strict mode (exit 4); by default invalid
  bytes are replaced with `U+FFFD`.
- `stdin` is read through the console `TextReader` (UTF-8 after configuration);
  `--encoding` does not apply to stdin.

## Chunk size is not as expected

- Active unit: `--max-tokens` > `--max-chars` > `--max-words` >
  `--max-bytes`.
- If no `--max-*` is set, `--max-tokens 512` is used.
- `--min-*` and `--drop-small` apply to the final chunk.
- `--prefix` is counted in the token limit (`prefix + chunk`).
- `--overlap-*` must be smaller than the corresponding `--max-*`, otherwise
  exit 2.
- `--trim` is on by default; `--no-trim` preserves edge whitespace.

## Memory and large files

Reading is streaming: memory is bounded by the chunking window, not by the file
size (for one giant line — by its size). Guidelines (Debug, local machine,
`--strategy fixed --max-chars 1000 --tokenizer char`):

| Input | Time | Peak RSS | Chunks |
|---|---:|---:|---:|
| 10 MB, lines | 0.7 s | 46.6 MiB | 7 282 |
| 100 MB, lines | 3.6 s | 49.8 MiB | 72 818 |
| 1 GB, lines | 30.0 s | 48.6 MiB | 745 655 |
| 10 MB, single line | 0.5 s | 140.9 MiB | 7 282 |

## Semantic chunking: embedding server (exit 5)

`--strategy semantic` does not run the model itself — it calls an HTTP server.
If there are no chunks and stderr contains an error, check:

1. The server is running and responds:
   ```bash
   curl http://127.0.0.1:9932/health
   ```
2. `--embedding-endpoint` matches the server address and
   `--embedding-model` — its `-a`/`/v1/models` (e.g., `embeddinggemma-2`).
3. The server is built with `--embedding` (otherwise `/v1/embeddings`
   returns 501/404).
4. If the server is locked behind `--api-key`, the environment variable
   `TEXTCHUNKER_EMBEDDING_API_KEY` is set in the **process** environment.
5. The response dimension matches `--embedding-dimensions` (default 768).

Exit code — 5; the API returns 503 `application/problem+json`. The first
request after a server restart may take ~10 s (CUDA warmup) — this is not a
timeout.

## 401 Unauthorized for semantic requests in the API

If `TEXTCHUNKER_EMBEDDING_API_KEY` is set on the server, versioned requests
to `/api/v1/chunk*` with `strategy=semantic` require the `X-API-Key` header
with the same value. Without it or with a wrong value the API responds
**401** `application/problem+json`.

- Add `-H "X-API-Key: $TEXTCHUNKER_EMBEDDING_API_KEY"` to the request.

## Too few or too many semantic boundaries

- **Too few boundaries** → decrease `--semantic-percentile` (e.g., `0.8`) or
  set `--semantic-window 1` (compare neighboring atoms).
- **Too many small chunks** → increase the percentile (`0.95`) and/or
  `--semantic-min-distance`.
- Absolute thresholds do not help: for `embeddinggemma`, unrelated texts have
  cosine 0.65–0.72, related ones — 0.95+. So thresholds are always relative
  (percentile).
- Look at `--stats`: `semanticAtomCount` shows how many atoms the text was
  split into, `embeddingCacheHits`/`embeddingCacheMisses` — the cost of the
  run.

## Embedding cache

- By default: `%LOCALAPPDATA%\TextChunker\embeddings\<model>-<hash>\<sha256>.bin`.
- The scope includes the endpoint, model name and dimension: two models with
  the same name on different servers do not reuse each other's vectors.
- `--embedding-cache-dir` overrides the directory; `--no-embedding-cache`
  disables the cache.
- The key includes the model, prompt and the atom's text; changing the
  model/prompt produces misses.
- Corrupted or dimension-mismatched entries are removed and recomputed.
- Maintenance: `textchunker --prune-cache --embedding-cache-ttl-days 30
  --embedding-cache-max-bytes 500000000` (add `--prune-cache-dry-run` to
  check). The limits also enable auto-cleanup before the run; the last
  entry is not deleted even if it exceeds the limit.
- For CI and offline checks use `--embedding-provider hash`.

## Document larger than the semantic limit (exit 2 / 413)

Semantic chunking works in document-mode: all text is buffered up to
`--semantic-max-bytes` (default 20 MB).

- `--semantic-fallback recursive` (default) — warning to stderr, chunking with
  `recursive`, `semanticFallback: "recursive"` in metadata.
- `--semantic-fallback error` — exit 2, API 413; no chunks are emitted.
- `--semantic-fallback streaming` — windows of
  `--semantic-stream-window-bytes` (4 MB) with overlap; memory is bounded by
  the window, `--semantic-max-bytes` does not limit file size. At window
  borders boundaries may be less accurate.
- For very large files streaming is usually worse than `recursive`; for small
  documents keep document-mode.

## 429, timeouts and retries

- Network errors, `429 Too Many Requests` and 5xx are retried with
  exponential backoff (200 ms → 5 s); retry count —
  `--embedding-max-retries` (default 3).
- `--embedding-timeout` (seconds, default 120) bounds one HTTP request;
  after retries are exhausted the CLI exits with code 5, the API responds 503.
- `llama.cpp` normally does not return 429 (requests queue up in slots), but
  429 can appear behind a reverse-proxy/rate-limiter. For parallel batches
  start the server with `--parallel N` and set
  `--embedding-concurrency` no higher than N.
- With `--embedding-concurrency > 1` the order of results is preserved;
  identical texts are deduplicated.
- If the server returns a context error for a large batch, decrease
  `--embedding-max-batch-chars` (e.g., to 4000) or `--embedding-batch-size`.
  An `input ... too large to process` error is split by the provider
  automatically (batch → text → averaging of partial vectors); if it does not
  help, increase the server's `-b`.

## `/scalar` is not available

- Documentation is enabled by default; if `ApiDocs__Enabled=false` is set,
  `/scalar` and `/openapi/v1.json` respond with 404 — this is expected.
- UI route is configured by `ApiDocs__Route` (default `/scalar`), document —
  by `ApiDocs__OpenApiRoute` (must contain `{documentName}`).
- Check the document itself:
  `curl http://localhost:5080/openapi/v1.json`.
- Scalar assets are served locally; no CDN or internet needed. If the UI is
  empty, check the browser console: usually the cause is a wrong
  `OpenApiRoute` or a reverse-proxy trimming the path.
- The root `/` redirects to the UI; with documentation disabled the root is
  not mapped.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | General error / stdin not redirected |
| 2 | Invalid arguments (including the semantic limit with `fallback=error`) |
| 3 | File not found |
| 4 | Read error / invalid encoding |
| 5 | Tokenizer loading error **or embedding provider error** |
| 6 | Write error |

## Useful commands

```bash
textchunker --help
textchunker --version
textchunker --input doc.txt --stats        # stats to stderr
```
