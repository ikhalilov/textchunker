# Architecture

## Overview

`TextChunker` is a streaming pipeline: text source → tokenizer → chunking
strategy → writer. Modules are separated by interfaces, so the tokenizer, the
strategy and the output format can be changed independently.

```
ITextSource.Lines()
        │  (content, startChar, endChar) in the "\n"-joined space
        ▼
ChunkingService  ── buffer + absolute offsets + metadata/seq/stats
        │
        ▼
IChunker.Chunk(text, tokenizer, options, final)
        │  ChunkSplit { Segments, ConsumedEnd, MoreNeeded }
        ▼
Chunk  ──►  IChunkWriter (JSON Lines / text)

Wrappers (hosts):
  CLI (src/TextChunker)        ── arg parsing, file/stdin sources, JSONL/text, exit codes
  API (src/TextChunker.Api)    ── Minimal APIs, in-memory source, JSON/NDJSON

Engine (src/TextChunker.Common) holds Sources/, Tokenizers/, Chunking/,
Writers/, Embeddings/; hosts reference Common + Abstractions only.
```

## Layers

### Sources (`Sources/`)

- `ITextSource` — `Lines()`: `(Content, StartChar, EndChar)`.
- `FileTextSource` — file, encoding, BOM; errors → exit 3/4/2.
- `StdinTextSource` — `Console.In`; no redirection → exit 1.
- `TextLines` — `StreamReader`, UTF-8 BOM stripping, `--invalid-encoding`,
  NFC/NFKC/line-endings/whitespace normalization.
- Lines are joined with `"\n"`; `StartChar`/`EndChar` — offsets in this space
  (`EndChar` exclusive). Chunk offsets are based on it.

### Tokenizers (`Tokenizers/`)

- `ITokenizer`: `CountTokens`, `CutAtTokenBoundary(text, n)` (0-based),
  `Name`, `ModelName`.
- `none`, `char` (code points, ZWJ/flags/skin tones), `word`, `regex` —
  approximate.
- `huggingface` — exact BPE via `Microsoft.ML.Tokenizers` +
  `tokenizer.json`: cache in `--tokenizer-cache-dir`, loaded once, then
  offline; errors → `TokenizerLoadException` (exit 5).
- `TokenizerFactory` — names and `ArgumentOutOfRangeException` for unknown
  ones (exit 2).
- `ChunkTokenCount.ForFinalChunk` — final recount of `prefix + text`.

### Chunking (`Chunking/`)

- `ChunkingOptions` — unit (`Unit`), `MaxSize`, min/overlap, `Trim`,
  `DropSmall`, `Prefix`, `Separators`.
- `SizeMeasure` — size in the active unit; for tokens the prefix is taken
  into account; `MaxCut` gives the maximal prefix that fits in the limit
  (surrogate-safe).
- `FixedChunker` — fixed chunks + overlap.
- `SeparatorChunker` — common recursive engine: separator hierarchy, greedy
  packing, hard-split fallback, overlap between emitted chunks.
- `RecursiveChunker` — `SeparatorChunker` with `options.Separators`.
- `ParagraphChunker` — first `\n\n`, then the remaining separators.
- **Semantic** (`Chunking/Semantic/`): `SemanticAtomizer` (Markdown/sentences/
  code/abbreviations), `SemanticPlanner` (embeddings → `1 − cos` →
  percentile/window), `SemanticPlan`, `SemanticChunker`. Embeddings
  (`Embeddings/`): `IEmbeddingProvider`, `LlamaCppEmbeddingProvider` (HTTP
  `/v1/embeddings`, batches/concurrency, text deduplication, retries on
  429/5xx, timeout), `HashEmbeddingProvider` (offline),
  `EmbeddingCache` + `CachedEmbeddingProvider`.
- `ChunkerFactory` — `fixed|recursive|paragraph|semantic` (recursive by
  default).
- `ChunkingOptionsValidator` — `max > 0`, `min <= max`, `overlap < max`
  (exit 2).

### Service (`ChunkingService`)

- Reads lines streamingly, maintains a buffer and `bufferStart` (absolute
  offsets).
- Calls the strategy until it stops making progress; `final=true` at EOF.
- Assembles `Chunk`: `id = "{source}:{seq:D6}"`, `seq`, `source`, `text`,
  `startChar`, `endChar`, `charCount`, `byteCount`, `tokenCount`,
  `metadata`.
- `metadata`: strategy, tokenizer/model, unit, `maxSize`, `normalized`,
  `prefix`/`prefixTokens`, user keys. `--metadata source=` overrides
  `source`/`id`.
- Keeps the last two chunks for `--min-*`/`--drop-small`; on cancellation
  (Ctrl+C) it writes out the already assembled complete chunks.
- Computes stats (`--stats`): `chunkCount`, `min/avg/maxChunkTokens`,
  `inputBytes`, `inputChars`, `elapsedMs`.

### Writers (`Writers/`)

- `JsonLinesWriter` — one JSON object per line, UTF-8 without BOM,
  camelCase fields.
- `TextChunkWriter` — `=== CHUNK NNNNNN ===` + text.
- Both write a complete line per operation — there are no partial JSON
  records.

### CLI (`Cli/`)

- `CliRunner` on `System.CommandLine`: argument parsing, default values,
  checks for `--invalid-encoding`/`--format`/`--normalize`/`--metadata`,
  pipeline start.
- Codes: 0 success; 2 arguments; 3 file; 4 read/encoding; 5 tokenizer or
  embedding provider error; 6 write; 1 other.
- When stdout/stderr are redirected — UTF-8 without BOM; the interactive
  console is switched to UTF-8.
- `--version` prints `AssemblyInformationalVersion`; `--help` — standard.

### API (`src/TextChunker.Api/`)

- ASP.NET Core Minimal APIs (net11.0), references
  `TextChunker.Common`/`TextChunker.Abstractions`.
- Routes grouped under `/api/v1` (`app.MapGroup`):
  `POST /api/v1/chunk` — JSON `{chunks, stats}`;
  `POST /api/v1/chunk/stream` — NDJSON (`application/x-ndjson`) via a bounded
  `Channel<Chunk>` (backpressure, async writes);
  `POST /api/v1/chunk/file` — multipart upload of `.txt`/`.md`/`.markdown`
  (encodings, BOM); `GET /api/v1/health|tokenizers|strategies`.
- `SemanticApiKeyFilter` — a single endpoint filter for `X-API-Key`
  validation on semantic requests when `TEXTCHUNKER_EMBEDDING_API_KEY` is
  set.
- `ChunkingEngine` assembles `ChunkingOptions`/`ITokenizer`/`IChunker` from
  `ChunkRequest`; `InMemoryTextSource` provides lines with absolute offsets;
  `TokenizerCache` (singleton) reuses loaded tokenizers. Settings are read
  via `IOptionsMonitor`.
- Errors — RFC 7807: 400 (validation), 401 (semantic without a key), 413
  (`MaxTextChars`/body), 415 (file extension), 503 (tokenizer or embedding
  provider), 500.
- Documentation: Swashbuckle generates OpenAPI (`/openapi/v1.json`), Scalar
  UI — `/scalar`; XML comments of contracts land in schema descriptions.
  Enabled via the `ApiDocs` section.
- Limitations: request text is not logged; no persistence and no background
  tasks.

## Streaming Chunking Contract

`ITokenizer` and `IChunker` — stateless; state (buffer, offsets, seq) is
held by the service.

`ChunkSplit`:
- `Segments` — ready chunks with **relative** offsets inside the passed text;
- `ConsumedEnd` — how many characters of the buffer have been processed
  (the service does `buffer.Remove(0, ConsumedEnd)`);
- `MoreNeeded` — the fragment ended inside a potential chunk, more text is
  needed;
- `final` — input is over, the remainder must be emitted.

Invariants: the limit is respected; no empty chunks; offsets never decrease;
if the buffer is non-empty the strategy either emits a chunk or returns
`MoreNeeded`/`ConsumedEnd` with progress.

## Extension Points

- **Tokenizer:** implement `ITokenizer`, add the name to
  `TokenizerFactory.SupportedNames`.
- **Strategy:** implement `IChunker`, add the name to `ChunkerFactory`.
- **Output format:** implement `IChunkWriter`, select it in
  `CliRunner.CreateWriter`.
- **Source:** implement `ITextSource` (lines + offsets in the
  join space).

## Project Layout

```
textchunker/
├── src/TextChunker.Abstractions/   interfaces, Chunk, ChunkingOptions, ChunkTokenCount
├── src/TextChunker.Common/         shared engine (net11.0 class library)
│   ├── Sources/                    file/stdin, encodings, BOM, normalization
│   ├── Tokenizers/                 none/char/word/regex/huggingface + factory
│   ├── Chunking/                   strategies, SizeMeasure, service, validation
│   ├── Writers/                    JSONL/text
│   └── Embeddings/                 llama-cpp/hash providers, cache
├── src/TextChunker/                CLI host: Cli/ + Program.cs (System.CommandLine)
├── src/TextChunker.Api/            Minimal APIs: contracts, services, Program.cs
├── tools/SemanticCalibration/      semantic calibration tool (Common + Abstractions)
├── tests/TextChunker.Tests/        unit + property + cancellation
├── tests/TextChunker.IntegrationTests/  CLI processes, golden, load tests
├── tests/TextChunker.Api.Tests/    HTTP tests (Kestrel + HttpClient)
├── docs/                           usage, building, chunking-strategies, tokenizers, api, troubleshooting, architecture
├── README.md
└── TextChunker.slnx
```

## Tests and CI

- Unit: tokenizers, chunkers, service, property invariants, cancellation.
- Integration: a real `dotnet TextChunker.dll` process — exit codes, JSONL,
  stdin, `--stats`, golden, 10 MB (100 MB/1 GB via
  `TEXTCHUNKER_LOAD_TESTS`).
- API: launching a real `TextChunker.Api.dll` on a free port and HTTP
  checks (health, chunk, stream, 400/401/413/415/503, source/metadata,
  `X-API-Key`, tokenizer cache).
- CI (`.github/workflows/ci.yml`): Ubuntu/Windows matrix, .NET 11
  (`net11.0`, SDK for `.slnx`).

## Future Improvements

Recommendations from the `dotnet-best-practices`,
`dotnet-design-pattern-review`, `csharp-concurrency-patterns` and
`csharp-nullable-reference-types` reviews:

- migrate `ChunkingEngine` and other services to primary-constructor
  syntax — done;
- add data-annotation validation to `ApiOptions` and enable
  `ValidateDataAnnotations()` — done;
- add `ConfigureAwait(false)` to async calls in library code — checked,
  already used;
- extend XML documentation for the remaining public types and methods — done
  for the key API types;
- evaluate a lightweight command-handler abstraction for future CLI commands
  — future work;
- evaluate a repository abstraction when persistence appears — future work;
- evaluate `ResourceManager` for log and error-message localization —
  future work.

The up-to-date checklist is kept in
`openspec/changes/api-design-recommendations/tasks.md`.
