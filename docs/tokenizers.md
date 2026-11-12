# Tokenizers

## Modes

| `--tokenizer` | Unit | Precision | Description |
|---|---|---|---|
| `huggingface` | model tokens | exact | `Microsoft.ML.Tokenizers` + `tokenizer.json` |
| `word` (default) | words | approximate | maximal runs of non-whitespace characters |
| `char` | code points | approximate | per Unicode code points (runes); emoji sequences — 1 token |
| `regex` | matches | approximate | `[\p{L}\p{N}]+` (words without punctuation) |
| `none` | — | — | `CountTokens = 0`, token limits do not apply |

If `--tokenizer` is not specified, `word` is used and a warning about the
approximation is written to stderr.

## huggingface (exact mode)

```bash
textchunker --input doc.txt \
  --tokenizer huggingface \
  --tokenizer-model google/embeddinggemma-2 \
  --max-tokens 512
```

- Default model — `google/embeddinggemma-2` (byte-level BPE, vocabulary 262 144,
  `byte_fallback: true`).
- `tokenizer.json` (~32 MB) is downloaded once:
  `https://huggingface.co/<model>/resolve/main/tokenizer.json`.
- Cache: `--tokenizer-cache-dir` or by default
  `%LOCALAPPDATA%/TextChunker/tokenizers/<sanitized-model>/tokenizer.json`
  (Linux/macOS — `$XDG_DATA_HOME` / `~/.local/share`, via `LocalApplicationData`).
- After the initial download, no network access is required — the file is read
  from the cache.
- A local file can be specified directly:

```bash
textchunker --input doc.txt --tokenizer huggingface \
  --tokenizer-path ./tokenizer.json --max-tokens 512
```

If the model could not be downloaded, read, or initialized, an error is written
to stderr, exit code — 5.

### Why the Count Is Exact

`Microsoft.ML.Tokenizers` reads the same `tokenizer.json` and implements the same
BPE engine as HF `transformers`/`tokenizers` (Rust). Token counting is performed
with the chosen tokenizer, without heuristics such as `symbols / 4`.

Verified reference values (model `google/embeddinggemma-2`):

| Text | Tokens |
|---|---|
| `hello world` | 2 |
| `hello  world` | 2 (repeated whitespace collapses) |
| `a b c` | 3 |
| `日本語` | 9 (byte fallback: 3 characters × 3 bytes) |
| `one\n\n two` | 2 |

Whitespace by itself is not a separate token.

### Limits and task prefix

When `--prefix` is specified, the limit is applied to `prefix + chunk.text`:

```
tokenCount(prefix + chunk.text) <= --max-tokens
```

Chunk metadata: `prefix`, `prefixTokens`. The final `tokenCount` of each chunk
is recomputed for the resulting text (gluing can change the token count at the
boundary).

## Relationship with embeddings (semantic)

Tokenizer and embedding provider are **independent** subsystems:

- `--tokenizer huggingface` is needed only for exact chunk-limit counting and
  reads `tokenizer.json` locally (or from the HF cache).
- `--strategy semantic` sends the **text** of atoms to the embedding server
  (`/v1/embeddings`); the server tokenizes it with its own model.
- The server does not need the same `tokenizer.json`, and the CLI does not need
  `--tokenizer-path` for the server.
- If `embeddinggemma-2` is used in both places, it is convenient to specify
  one local file for the CLI and run llama.cpp with the same GGUF.

## Cache and offline

```
%LOCALAPPDATA%\TextChunker\tokenizers\google_embeddinggemma-2\tokenizer.json
```

Deleting the directory will trigger a re-download on the next run. For a fully
offline setup, copy `tokenizer.json` into the cache manually or use
`--tokenizer-path`.

## Performance

- Loading the 262k vocabulary and ~515k merge rules takes about a second and a
  few hundred MB of memory; it happens once per process.
- Token counting is O(text length); chunking performs several counts per chunk.
- For very large files, use `--max-tokens` and the streaming source: chunks
  are emitted as they are read.
