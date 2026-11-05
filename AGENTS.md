# AGENTS.md

This file contains a compact, **actionable** guide that helps future OpenCode agents avoid common pitfalls when working with the **TextChunker** repository.  Each bullet answers the question *“Would an agent likely miss this without help?”* – if the answer is *yes*, the bullet is included; otherwise it is omitted.

---

## Repository Overview

- **Monorepo** – single solution `TextChunker.slnx` contains three projects:
  - `src/TextChunker` – CLI (`TextChunker.exe`)
  - `src/TextChunker.Api` – Minimal‑API web service
  - `src/TextChunker.Abstractions` – shared interfaces and types
- **Target framework**: `.NET 10` (runtime required for CLI & API, tests use the same SDK)
- **Build artefacts**: `dotnet publish` outputs to `out/` (framework‑dependent) or `out/<rid>/` for cross‑platform builds.
- **Tests**: xUnit v3 (`Microsoft.Testing.Platform`) – unit, integration, and API tests.
- **Documentation**: `docs/` contains Markdown guides (`usage.md`, `chunking‑strategies.md`, etc.).

---

## Build & Run

| Action | Command | Notes |
|---|---|---|
| Build solution | `dotnet build TextChunker.slnx` | Compiles all projects. |
| Run CLI (interactive) | `dotnet run --project src/TextChunker` | Uses `System.CommandLine`; `--help` shows options. |
| Run API locally | `dotnet run --project src/TextChunker.Api --urls http://localhost:5080` | Default port 5080; `Scalar` UI at `/scalar`. |
| Publish CLI (framework‑dependent) | `dotnet publish src/TextChunker -c Release -o out` | Requires .NET 10 runtime. |
| Publish cross‑platform (framework‑dependent) | `dotnet publish src/TextChunker -c Release -r win-x64 -o out/win-x64` | Repeat for `linux-x64` / `osx-x64`. |
| Publish self‑contained | `dotnet publish src/TextChunker -c Release -r linux-x64 --self-contained true -o out/linux-x64-sc` | Runtime bundled. |

---

## CLI Usage

The CLI reads from a file or `stdin`, writes JSONL (default) or plain text, and supports a rich set of options.

| Option | Meaning | Typical value |
|---|---|---|
| `--input <path>` | Input file; `-` or omitted → `stdin` | `document.txt` |
| `--output <path>` | Output file; default `stdout` | `chunks.jsonl` |
| `--strategy` | Chunking strategy: `fixed`, `recursive` (default), `paragraph`, `semantic` | `recursive` |
| `--tokenizer` | Tokenizer mode: `huggingface`, `word` (default), `char`, `regex`, `none` | `huggingface` |
| `--tokenizer-model` | HuggingFace model ID (used when `huggingface`) | `google/embeddinggemma-2` |
| `--tokenizer-path` | Path to local `tokenizer.json` | `./tokenizer.json` |
| `--max-tokens`, `--max-chars`, `--max-words`, `--max-bytes` | Chunk size unit (priority: tokens > chars > words > bytes) | `--max-tokens 512` |
| `--min-tokens`, `--min-chars`, `--min-words` | Minimum size for the *final* chunk | `--min-tokens 50` |
| `--drop-small` | Discard final chunk if below `--min-*` | `--drop-small` |
| `--trim` / `--no-trim` | Trim whitespace from chunk edges (default `--trim`) | `--trim` |
| `--overlap-tokens`, `--overlap-chars` | Overlap between adjacent chunks | `--overlap-tokens 20` |
| `--prefix <text>` | Task prefix; counted in token limit | `--prefix "title: none | text: "` |
| `--metadata key=value` | User metadata added to each chunk | `--metadata lang=ru` |
| `--format jsonl` / `--format text` | Output format (default `jsonl`) | `--format text` |
| `--stats` | Emit aggregated stats to `stderr` | `--stats` |
| `--encoding <codepage>` | Input encoding (UTF‑8 default) | `--encoding windows-1251` |
| `--invalid-encoding error` | Fail on invalid bytes (default `replace`) | `--invalid-encoding error` |
| `--normalize` | Normalization (`nfkc`, `line-endings`, etc.) | `--normalize nfkc` |
| `--strategy semantic` | Semantic chunking – requires embedding provider | See **Semantic Options** below |

### Semantic Options (when `--strategy semantic`)

| Option | Meaning | Typical value |
|---|---|---|
| `--embedding-provider` | `llama-cpp` (default) or `hash` (offline) | `llama-cpp` |
| `--embedding-endpoint` | Base URL of embedding server | `http://127.0.0.1:9932` |
| `--embedding-model` | Model name for the server | `embeddinggemma-2` |
| `--embedding-batch-size` | Atoms per HTTP request | `32` |
| `--embedding-max-batch-chars` | Max chars per batch (0 = no limit) | `8000` |
| `--embedding-concurrency` | Parallel HTTP batches | `1` |
| `--embedding-timeout` | Seconds per request | `120` |
| `--embedding-max-retries` | Retries on 429/5xx | `3` |
| `--semantic-percentile` | Percentile of distance for boundaries | `0.90` |
| `--semantic-min-distance` | Minimum cosine distance | `0.02` |
| `--semantic-window` | TextTiling window (atoms) | `1` |
| `--semantic-max-bytes` | Document buffer limit (default 20 MB) | `20000000` |
| `--semantic-fallback` | `recursive` (default), `error`, or `streaming` | `recursive` |
| `--semantic-stream-window-bytes` | Window size for streaming mode | `4000000` |
| `--semantic-stream-overlap-bytes` | Overlap between windows | `256000` |
| `--no-embedding-cache` | Disable caching | `--no-embedding-cache` |

**Important**: The CLI will *fail* (exit 5) if the embedding server is unreachable or the tokenizer fails to load.  The `llama.cpp` server must be running on the URL specified by `--embedding-endpoint`.

---

## API Usage

The API is a minimal‑API web service that exposes the same chunking engine as the CLI.

| Endpoint | Method | Description |
|---|---|---|
| `/api/v1/health` | `GET` | Returns `{status, version}` |
| `/api/v1/tokenizers` | `GET` | `{names, defaultModel}` |
| `/api/v1/strategies` | `GET` | `{names}` (strategies) |
| `/scalar` | `GET` | Scalar UI for OpenAPI |
| `/openapi/v1.json` | `GET` | OpenAPI document |
| `/api/v1/chunk` | `POST` | JSON body → `{chunks, stats}` |
| `/api/v1/chunk/stream` | `POST` | NDJSON stream of chunks |
| `/api/v1/chunk/file` | `POST` | Multipart upload (`file` + options) |

Semantic requests (`/api/v1/chunk*` with `strategy=semantic`) require `X-API-Key` when
`TEXTCHUNKER_EMBEDDING_API_KEY` is set.

### Example: POST `/api/v1/chunk`

```bash
curl -X POST http://localhost:5080/api/v1/chunk \
  -H "Content-Type: application/json" \
  -d '{"text":"Hello world","strategy":"recursive","maxTokens":512}'
```

### Example: POST `/api/v1/chunk/file`

```bash
curl -X POST http://localhost:5080/api/v1/chunk/file \
  -F "file=@document.md" \
  -F "strategy=semantic" \
  -F "tokenizer=huggingface" \
  -F "tokenizerModel=google/embeddinggemma-2" \
  -F "maxTokens=512" \
  -F "embeddingProvider=llama-cpp" \
  -F "embeddingEndpoint=http://127.0.0.1:9932" \
  -F "embeddingModel=embeddinggemma-2" \
  -F "encoding=utf-8"
```

**Notes**:
- Request body size is limited to 20 MB (`Chunking:MaxTextChars`).  Exceeding this returns **413**.
- `multipart/form-data` accepts only `.txt`, `.md`, `.markdown` extensions; otherwise **415**.
- Errors are returned as `application/problem+json` (RFC 7807).
- Semantic requests (`/api/v1/chunk*`) require the `X-API-Key` header when
  `TEXTCHUNKER_EMBEDDING_API_KEY` is configured; otherwise they return **401**.
- `X-API-Key` validation is centralized in `SemanticApiKeyFilter` and applied to all semantic
  endpoints; do not duplicate it inside individual handlers.
- The API inherits the same environment variables as the CLI (`TEXTCHUNKER_LLAMA_CPP_URL`, `TEXTCHUNKER_LLAMA_CPP_MODEL`, `TEXTCHUNKER_EMBEDDING_API_KEY`).

---

## Testing

### Test Projects
| Project | Purpose |
|---|---|
| `tests/TextChunker.Tests` | Unit & property tests for core logic |
| `tests/TextChunker.IntegrationTests` | Integration tests that spawn the CLI and API, use golden fixtures |
| `tests/TextChunker.Api.Tests` | HTTP tests for the API endpoints |

### Running Tests

```bash
# All tests (unit + integration + API)
dotnet test TextChunker.slnx

# Only unit tests
# dotnet test tests/TextChunker.Tests

# Only integration tests (requires llama.cpp server)
# dotnet test tests/TextChunker.IntegrationTests

# Only API tests
# dotnet test tests/TextChunker.Api.Tests
```

#### Running a single test
```bash
# Example: run a single test method
# Replace <TestFullName> with the full name (namespace + class + method)
dotnet test tests/TextChunker.Tests --filter <TestFullName>
```

#### Large integration tests
Integration tests include large file workloads (10 MB, 100 MB, 1 GB).  They are disabled by default.  Enable them with:
```bash
export TEXTCHUNKER_LOAD_TESTS=1        # 100 MB + 10 MB line
export TEXTCHUNKER_LOAD_TESTS=1GB      # + 1 GB
```

#### Environment requirements for integration tests
- `TEXTCHUNKER_LLAMA_CPP_URL` and `TEXTCHUNKER_LLAMA_CPP_MODEL` must point to a running `llama.cpp` server.
- `tokenizer.json` must exist in the checkout (otherwise tests that depend on the HuggingFace tokenizer are skipped).
- For API tests, the API must be running; tests start a self‑contained server.

---

## Common Pitfalls & Debugging

| Issue | Cause | Fix |
|---|---|---|
| `stdin не переопределён` (exit 1) | No `--input` and no stdin redirection | Provide `--input <file>` or pipe input (`cat file | textchunker`). |
| `exit 5` – tokenizer or embedding provider error | `tokenizer.json` missing, server unreachable, wrong model ID | Run `scripts/fetch-tokenizer.ps1`/`.sh` to download the file; start `llama.cpp` with correct flags; set `TEXTCHUNKER_LLAMA_CPP_URL` and `TEXTCHUNKER_LLAMA_CPP_MODEL`. |
| `401` – unauthorized semantic request | `TEXTCHUNKER_EMBEDDING_API_KEY` is set but `X-API-Key` is missing or wrong on `/api/v1/chunk*` | Send `X-API-Key` with the same value. |
| `413` – request too large | Input exceeds `Chunking:MaxTextChars` (20 MB) | Reduce input size or use `--semantic-fallback streaming`. |
| `415` – unsupported file extension | File uploaded to `/api/v1/chunk/file` has unsupported extension | Use `.txt`, `.md`, or `.markdown`. |
| `?` characters in console | Console encoding not set to UTF‑8 | Run `chcp 65001` (Windows) or configure terminal to UTF‑8. |
| `semantic` strategy fails on large documents | Document > `--semantic-max-bytes` and fallback set to `error` | Set `--semantic-fallback streaming` or reduce file size. |
| `semantic` strategy gives many tiny chunks | `--semantic-percentile` too high or `--semantic-min-distance` too low | Decrease percentile or increase min distance. |
| `semantic` strategy produces no chunks | `llama.cpp` server not running or wrong `embedding-endpoint` | Verify server health (`curl http://127.0.0.1:9932/health`) and set correct URL. |
| API returns 500 on `POST /api/v1/chunk` | Runtime exception in the server | Check server logs; ensure all required env vars are set. |

---

## Useful References

- **README.md** – high‑level overview and quick start.
- **docs/usage.md** – CLI command examples.
- **docs/chunking‑strategies.md** – strategy details and limits.
- **docs/tokenizers.md** – tokenizer modes and HuggingFace integration.
- **docs/semantic‑chunking.md** – semantic strategy, embedding server, tuning.
- **docs/api.md** – HTTP API contract and examples.
- **docs/troubleshooting.md** – common issues and fixes.
- **docs/architecture.md** – internal design and extensibility; see “Дальнейшие улучшения” for
  pending best-practice and design-pattern follow-ups.

---

## Quick Commands Cheat‑Sheet

```bash
# Build
  dotnet build TextChunker.slnx

# Run CLI (example)
  dotnet run --project src/TextChunker -- \
    --input document.md \
    --strategy semantic \
    --embedding-provider llama-cpp \
    --embedding-endpoint http://127.0.0.1:9932 \
    --embedding-model embeddinggemma-2 \
    --tokenizer huggingface \
    --tokenizer-path ./tokenizer.json \
    --max-tokens 512 \
    --stats

# Run API
  dotnet run --project src/TextChunker.Api --urls http://localhost:5080

# Publish CLI
  dotnet publish src/TextChunker -c Release -o out

# Run tests
  dotnet test TextChunker.slnx

# Run a single test
  dotnet test tests/TextChunker.Tests --filter Fully.Qualified.TestName

# Enable large integration tests
  export TEXTCHUNKER_LOAD_TESTS=1
```

---

**End of AGENTS.md**