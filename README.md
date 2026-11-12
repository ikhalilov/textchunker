# TextChunker

Cross-platform C# / .NET 10 console utility for streaming text chunking for RAG/vectorization.
Reads from a file or `stdin`, splits by strategy
(`fixed` / `recursive` / `paragraph` / `semantic`), measures size in tokens/characters/words/bytes,
and writes JSON Lines (or plain text).

## Features

- Streaming read without loading the whole file into memory.
- Semantic chunking (`--strategy semantic`) via local llama.cpp/`embeddinggemma-2`
  or the offline `hash` provider; for large files — windowed `--semantic-fallback streaming`.
- Accurate token counts for the target model: `huggingface` (`Microsoft.ML.Tokenizers` +
  `tokenizer.json` from `google/embeddinggemma-2`, byte-level BPE). The file is downloaded once
  into a cache, further work is fully offline.
- Approximate modes: `word` (default, with a warning to stderr), `char`, `regex`, `none`.
- Task prefix (`--prefix`), counted in the token limit; final `tokenCount` is
  recomputed for the resulting chunk text (`prefix + chunk`).
- Overlap (`--overlap-tokens` / `--overlap-chars`), minimum size (`--min-*`), `--drop-small`, `--trim`.
- Configurable `recursive` separators (`--separators`).
- Encodings (`--encoding`, UTF-8 by default), BOM, `--invalid-encoding replace|error`,
  optional normalization (`--normalize`).
- JSON Lines with fields `id`, `seq`, `source`, `text`, `startChar`, `endChar`, `charCount`,
  `byteCount`, `tokenCount`, `metadata`.
- `--stats` — aggregated stats to stderr (stdout stays clean JSONL);
  for `semantic` — also `semanticAtomCount` and embedding cache stats.
- Stable exit codes; errors and warnings go to stderr only.
- HTTP API (ASP.NET Core Minimal APIs) — `/api/v1/chunk`, `/api/v1/chunk/stream` (NDJSON), `/api/v1/health`.

## Documentation

| Document | Covers |
|---|---|
| [usage.md](docs/usage.md) | CLI usage: quick start, parameters reference, encodings, JSONL, stats, exit codes |
| [building.md](docs/building.md) | Build, running tests (including load tests), publishing |
| [chunking-strategies.md](docs/chunking-strategies.md) | `fixed` / `recursive` / `paragraph` / `semantic` strategies, units, overlap, invariants |
| [tokenizers.md](docs/tokenizers.md) | Tokenizer modes, exact `huggingface`, cache, offline |
| [semantic-chunking.md](docs/semantic-chunking.md) | Semantic chunking: embedding server startup, parameters, tuning, performance |
| [api.md](docs/api.md) | HTTP API: routes, requests/responses, authentication, errors |
| [troubleshooting.md](docs/troubleshooting.md) | Common issues, diagnostics, memory and large files |
| [architecture.md](docs/architecture.md) | Architecture, design, extension points |
