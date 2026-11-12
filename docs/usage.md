# Usage

## Quick Start

```bash
# stdin → stdout (word tokenizer by default, approximate)
cat document.txt | dotnet run --project src/TextChunker -- --input -

# file → JSONL file: 512 tokens of the target model
dotnet run --project src/TextChunker -- \
  --input document.txt --output chunks.jsonl \
  --tokenizer huggingface --tokenizer-model google/embeddinggemma-2 \
  --strategy recursive --max-tokens 512 --stats

# task prefix is counted in the limit
dotnet run --project src/TextChunker -- \
  --input document.txt --tokenizer huggingface \
  --prefix "title: none | text: " --max-tokens 512

# plain-text output with markers
dotnet run --project src/TextChunker -- --input document.txt --format text --max-chars 800

# semantic: local llama.cpp (server already started — see semantic-chunking.md)
dotnet run --project src/TextChunker -- \
  --input document.md --strategy semantic \
  --embedding-provider llama-cpp --embedding-endpoint http://127.0.0.1:9932 \
  --embedding-model embeddinggemma-2 \
  --tokenizer huggingface --tokenizer-path ./tokenizer.json --max-tokens 512
```

After publishing, the utility is invoked as `textchunker` (see
[building.md](building.md) for build and publishing).

## Parameter Reference

| Parameter | Description |
|---|---|
| `--input <path>` | Input file; `-` or omission — stdin |
| `--output <path>` | Output file (stdout by default) |
| `--strategy` | `fixed`, `recursive` (default), `paragraph`, `semantic` |
| `--tokenizer` | `none`, `char`, `word` (default), `regex`, `huggingface` |
| `--tokenizer-model` | HF model (default `google/embeddinggemma-2`) or path to `tokenizer.json` |
| `--tokenizer-path` | Explicit path to a local `tokenizer.json` |
| `--tokenizer-cache-dir` | Tokenizer cache directory |
| `--prefix` | Task prefix, counted in the token limit |
| `--max-tokens` / `--max-chars` / `--max-words` / `--max-bytes` | Maximum chunk size (priority: tokens > chars > words > bytes; if none set — 512 tokens) |
| `--min-tokens` / `--min-chars` / `--min-words` | Minimum size for the final chunk |
| `--overlap-tokens` / `--overlap-chars` | Overlap between neighboring chunks |
| `--trim` / `--no-trim` | Trim whitespace at edges (on by default) |
| `--drop-small` | Discard the final chunk smaller than `--min-*` |
| `--separators` | `recursive` separators in descending priority order ([details](chunking-strategies.md)) |
| `--format` | `jsonl` (default) or `text` |
| `--encoding` | Input encoding (default `utf-8`) |
| `--invalid-encoding` | `replace` (default) or `error` |
| `--normalize` | `nfc`, `nfkc`, `line-endings`, `whitespace`, `all` (comma-separated) |
| `--metadata key=value` | User metadata (multiple allowed; `source=` overrides `source`/`id`) |
| `--stats` | JSON stats to stderr |
| `--version`, `--help` | Version and help |

Semantic strategy parameters are in [semantic-chunking.md](semantic-chunking.md)
(section "Parameters").

## Input and Output

```bash
# file → stdout
textchunker --input document.txt

# stdin → stdout
cat document.txt | textchunker --input -
cat document.txt | textchunker            # stdin by default

# file → file (UTF-8 without BOM)
textchunker --input document.txt --output chunks.jsonl

# plain text with markers === CHUNK 000001 ===
textchunker --input document.txt --format text
```

If `--input` is not set and stdin is not redirected, the utility writes an error
to stderr and exits with code 1 (so it does not block on the terminal).

## Version and Help

```bash
textchunker --help
textchunker --version      # 0.2.0
```

## Encodings and BOM

- Input is decoded as UTF-8 by default; the UTF-8 BOM is stripped and does not
  enter the text.
- `--encoding windows-1251` (and other code pages, including `koi8-r`,
  `iso-8859-1`) is available via `CodePagesEncodingProvider`.
- Invalid sequences are replaced with `U+FFFD` by default;
  `--invalid-encoding error` stops processing with exit code 4.

## Normalization

By default the text is not modified (offsets match the original text after BOM
removal). `--normalize` enables normalization; in that case `startChar`/`endChar`
refer to the normalized text, and `normalized = true` in metadata.

```bash
textchunker --input doc.txt --normalize nfkc
textchunker --input doc.txt --normalize "line-endings,whitespace"
textchunker --input doc.txt --normalize all
```

## Limits and Units

The active unit is determined by the set `--max-*`
(priority: tokens > chars > words > bytes). If none is set —
`--max-tokens 512` is used.

```bash
textchunker --input doc.txt --max-chars 800
textchunker --input doc.txt --max-words 120
textchunker --input doc.txt --max-bytes 2048
textchunker --input doc.txt --tokenizer huggingface --max-tokens 512
```

Minimum size and final chunk:

```bash
# final chunk smaller than 50 tokens is discarded
textchunker --input doc.txt --max-tokens 512 --min-tokens 50 --drop-small

# without --drop-small the final chunk is kept (merged with the previous
# one where possible)
textchunker --input doc.txt --max-tokens 512 --min-tokens 50
```

Overlap:

```bash
textchunker --input doc.txt --strategy fixed --max-tokens 100 --overlap-tokens 20
textchunker --input doc.txt --strategy fixed --max-chars 500 --overlap-chars 100
```

## Task Prefix

`--prefix` is counted in the limit:
`tokenCount(prefix + chunk.text) <= max-tokens` is guaranteed. The `prefix`
and `prefixTokens` fields appear in metadata.

```bash
textchunker --input doc.txt --tokenizer huggingface \
  --prefix "title: none | text: " --max-tokens 512
```

## Metadata

```bash
textchunker --input doc.txt --metadata lang=ru --metadata project=kb
```

User keys end up in each chunk's `metadata` object. Service keys
(`strategy`, `tokenizer`, `tokenizerModel`, `unit`, `normalized`, `maxSize`,
`prefix`, `prefixTokens`) are added automatically.

`--metadata source=<name>` additionally overrides the top-level `source` field
and the prefix of the deterministic `id` (by default the source is the file
name or `stdin`).

## Semantic Chunking

```bash
# local llama.cpp
textchunker --input doc.md --strategy semantic \
  --embedding-provider llama-cpp \
  --embedding-endpoint http://127.0.0.1:9932 \
  --embedding-model embeddinggemma-2 \
  --embedding-batch-size 32 --embedding-concurrency 4 \
  --tokenizer huggingface --tokenizer-path ./tokenizer.json \
  --max-tokens 512 --prefix "title: none | text: " --stats

# offline (deterministic provider, no model)
textchunker --input doc.md --strategy semantic \
  --embedding-provider hash --no-embedding-cache --max-tokens 512
```

- Text is buffered up to `--semantic-max-bytes` (20 MB); a larger document
  falls back to `recursive` or finishes with an error (`--semantic-fallback`).
- Boundaries are configured with `--semantic-percentile`,
  `--semantic-min-distance`, `--semantic-window`.
- All usual guarantees are preserved: limits with `--prefix`, `--min-*`,
  `--drop-small`, `--trim`, `--overlap-*`, deterministic ids and offsets.
- Details — [semantic-chunking.md](semantic-chunking.md).

## Stats

`--stats` writes JSON to **stderr** (stdout stays clean JSONL):

```json
{"chunkCount":14,"minChunkTokens":16,"avgChunkTokens":31.5,"maxChunkTokens":40,"inputBytes":415,"inputChars":245,"elapsedMs":77}
```

For the semantic strategy additional diagnostic fields appear:

```json
{"chunkCount":8,"semanticAtomCount":23,"embeddingCacheHits":18,"embeddingCacheMisses":5}
```

`embeddingCacheHits`/`embeddingCacheMisses` appear only when the cache is
enabled; `semanticAtomCount` — only for `--strategy semantic`. For semantic,
`elapsedMs` includes building the plan (embeddings), i.e., it corresponds to
real wall-clock runtime.

## JSONL Format

One JSON record per chunk, one object per line, UTF-8 without BOM:

```json
{
  "id": "document.txt:000001",
  "seq": 1,
  "source": "document.txt",
  "text": "...",
  "startChar": 0,
  "endChar": 812,
  "charCount": 812,
  "byteCount": 1487,
  "tokenCount": 512,
  "metadata": { "...": "..." }
}
```

- `id` is deterministic: `{source}:{seq:D6}`.
- `seq` starts at 1.
- `startChar` / `endChar` — offsets in the original (or normalized) text;
  `endChar` is exclusive.
- `charCount` — length of the text in UTF-16 code units, `byteCount` — size in
  UTF-8.
- `tokenCount` — exact recount for the resulting `prefix + text` text.
- Offsets and `charCount` match `text` after `--trim` (edge whitespace is not
  part of the chunk).

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | General error / stdin not redirected |
| 2 | Invalid arguments |
| 3 | File not found |
| 4 | Read error / invalid encoding |
| 5 | Tokenizer loading or embedding provider error |
| 6 | Write error |

## Ctrl+C

On interruption, processing stops at a line boundary; already completed
chunks up to that point are written, and an incomplete JSON line never appears.
The exit code is 0.

## See Also

- [chunking-strategies.md](chunking-strategies.md) — strategies, units, overlap.
- [semantic-chunking.md](semantic-chunking.md) — semantic chunking, streaming,
  llama.cpp, tuning.
- [tokenizers.md](tokenizers.md) — tokenizer modes and model cache.
- [troubleshooting.md](troubleshooting.md) — encodings, stdin, exit codes,
  memory.
- [architecture.md](architecture.md) — design and extension points.
