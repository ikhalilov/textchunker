# HTTP API (Minimal APIs)

The `src/TextChunker.Api` project uses ASP.NET Core Minimal APIs (net11.0).
It uses the same chunking engine (`ChunkingService`, strategies, tokenizers)
as the CLI.

## Running

```bash
dotnet run --project src/TextChunker.Api --urls http://localhost:5080
```

Publishing and running:

```bash
dotnet publish src/TextChunker.Api -c Release -o out/api
./out/api/TextChunker.Api --urls http://0.0.0.0:5080
```

Configuration (`appsettings.json` / environment variables):

| Key | Default | Purpose |
|---|---|---|
| `Chunking:MaxTextChars` | `5 000 000` | maximum `text` length (otherwise 413) |
| `Chunking:TokenizerCacheDir` | default path | cache directory for `tokenizer.json` |
| `ApiDocs:Enabled` | `true` | publish OpenAPI/Scalar |
| `ApiDocs:Route` | `/scalar` | Scalar UI route |
| `ApiDocs:OpenApiRoute` | `/openapi/{documentName}.json` | OpenAPI route (`{documentName}` is required) |
| `ApiDocs:Title` | `TextChunker API` | documentation title |
| Kestrel `MaxRequestBodySize` | 20 MB | request body size limit |

## Versioning

The current API version — `/api/v1`. All clients should use routes with the
version prefix.

## Routes

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v1/health` | `{status, version}` |
| GET | `/api/v1/tokenizers` | `{names, defaultModel}` |
| GET | `/api/v1/strategies` | `{names}` — `fixed`, `recursive`, `paragraph`, `semantic` |
| GET | `/` | redirect to `/scalar` |
| GET | `/scalar` | Scalar UI (OpenAPI reference) |
| GET | `/openapi/v1.json` | OpenAPI 3.0 document |
| POST | `/api/v1/chunk` | chunking: JSON `{chunks, stats}` |
| POST | `/api/v1/chunk/stream` | NDJSON: one chunk per line |
| POST | `/api/v1/chunk/file` | upload of `.txt`/`.md`/`.markdown` (multipart) |

### Quick examples

```bash
# GET /api/v1/health
curl http://localhost:5080/api/v1/health

# GET /api/v1/tokenizers
curl http://localhost:5080/api/v1/tokenizers

# GET /api/v1/strategies
curl http://localhost:5080/api/v1/strategies

# POST /api/v1/chunk
curl -X POST http://localhost:5080/api/v1/chunk \
  -H "Content-Type: application/json" \
  -d '{"text":"Hello world. Second sentence.","strategy":"recursive","tokenizer":"word","maxTokens":512}'

# POST /api/v1/chunk/stream
curl -N -X POST http://localhost:5080/api/v1/chunk/stream \
  -H "Content-Type: application/json" \
  -d '{"text":"abcdefghij","strategy":"fixed","maxChars":4,"tokenizer":"char"}'

# POST /api/v1/chunk/file
curl -X POST http://localhost:5080/api/v1/chunk/file \
  -F "file=@document.md" \
  -F "strategy=recursive" \
  -F "tokenizer=word" \
  -F "maxTokens=512"

# GET /scalar
curl http://localhost:5080/scalar

# GET /openapi/v1.json
curl http://localhost:5080/openapi/v1.json

# GET / (redirect to /scalar)
curl -I http://localhost:5080/
```

For `strategy=semantic` add the `X-API-Key` header if `TEXTCHUNKER_EMBEDDING_API_KEY`
is set on the server:

```bash
curl -X POST http://localhost:5080/api/v1/chunk \
  -H "Content-Type: application/json" \
  -H "X-API-Key: $TEXTCHUNKER_EMBEDDING_API_KEY" \
  -d '{"text":"…","strategy":"semantic","tokenizer":"huggingface","maxTokens":512}'
```

## Interactive Documentation (Scalar)

- UI: `/scalar` (root `/` redirects to `/scalar`).
- OpenAPI 3.2: `/openapi/v1.json` (built-in `Microsoft.AspNetCore.OpenApi`).
- UI: [Scalar](https://scalar.com/); assets are served locally, no CDN.
- The document is grouped under the OpenAPI tags `Справочники` and `Нарезка`,
  with summaries and schema descriptions taken from XML comments,
  `problem+json` for 400/401/413/415/503, and NDJSON for `/api/v1/chunk/stream`.
- OpenAPI includes versioned routes under `/api/v1/...`.
- Disabling: `ApiDocs__Enabled=false` (documentation routes → 404, the API
  works).
- Behind a reverse proxy with a prefix, set `ApiDocs__Route` and
  `ApiDocs__OpenApiRoute` consistently; `OpenApiRoute` must contain
  `{documentName}`.

```bash
# see the document
curl http://localhost:5080/openapi/v1.json

# UI
start http://localhost:5080/scalar
```

### `POST /api/v1/chunk`

```json
{
  "text": "Текст для нарезки…",
  "strategy": "recursive",
  "tokenizer": "huggingface",
  "tokenizerModel": "google/embeddinggemma-2",
  "tokenizerPath": null,
  "tokenizerCacheDir": null,
  "prefix": "title: none | text: ",
  "maxTokens": 512,
  "minTokens": 50,
  "overlapTokens": null,
  "trim": true,
  "dropSmall": false,
  "separators": ["\n\n", "\n"],
  "metadata": { "lang": "ru" },
  "source": "document.md"
}
```

- All fields except `text` are optional.
- If no `max-*` is set → `maxTokens = 512`; default strategy is
  `recursive`, tokenizer `word`.
- `source` (default `api`) sets the top-level `source` and the `id` prefix
  (`{source}:{seq:D6}`).
- `metadata` is passed to the `metadata` of each chunk.
- For `huggingface`, `tokenizerPath` (local `tokenizer.json`) takes precedence
  over `tokenizerModel`; `tokenizerCacheDir` overrides the server's.

Response:

```json
{
  "chunks": [
    {
      "id": "document.md:000001",
      "seq": 1,
      "source": "document.md",
      "text": "…",
      "startChar": 0,
      "endChar": 812,
      "charCount": 812,
      "byteCount": 1487,
      "tokenCount": 512,
      "metadata": { "strategy": "recursive", "tokenizer": "huggingface", "…": "…" }
    }
  ],
  "stats": {
    "chunkCount": 1,
    "minChunkTokens": 512,
    "avgChunkTokens": 512,
    "maxChunkTokens": 512,
    "inputChars": 812,
    "inputBytes": 1487,
    "elapsedMs": 12
  }
}
```

For `strategy: "semantic"`, `stats` also includes `semanticAtomCount`,
`embeddingCacheHits`, `embeddingCacheMisses`; `elapsedMs` includes building
the semantic plan (embeddings).

### `POST /api/v1/chunk/stream`

The same request; the response is `200 application/x-ndjson`, each line is
a JSON chunk (format like `chunks[]` items), `\n` separated. Delivery is
streaming: bounded channel (backpressure), memory is bounded by the chunking
window; client disconnect stops the processing.

```bash
curl -N -X POST http://localhost:5080/api/v1/chunk/stream \
  -H "Content-Type: application/json" \
  -d '{"text":"abcdefghij","strategy":"fixed","maxChars":4,"tokenizer":"char"}'
```

### `POST /api/v1/chunk/file`

Upload a file `.txt`, `.md`, or `.markdown` as `multipart/form-data`. The
part with the file is `file`; options are regular form fields (the same as
`/api/v1/chunk`, plus `encoding`/`invalidEncoding`).

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
  -F "embeddingConcurrency=4" \
  -F "semanticPercentile=0.9" \
  -F "source=document.md" \
  -F "encoding=utf-8"
```

- Extensions: `.txt`, `.md`, `.markdown` (case-insensitive); otherwise
  **415**.
- Encoding is UTF-8 by default, BOM is stripped; `encoding=windows-1251` and
  other code pages; `invalidEncoding=replace|error` (default `replace`;
  `error` → 400).
- `separators` and `metadata` are passed as JSON strings:
  `-F 'separators=["\n\n","\n"]'`, `-F 'metadata={"lang":"ru"}'`.
- For `semantic`, flat fields (`embeddingProvider`,
  `embeddingDimensions`, `embeddingMaxBatchChars`, `embeddingConcurrency`,
  `embeddingTimeoutSeconds`, `embeddingMaxRetries`, `semanticPercentile`,
  `semanticWindow`, `semanticMaxBytes`, `semanticStreamWindowBytes`,
  `semanticStreamOverlapBytes`, `semanticFallback`, …) or JSON strings
  `embedding` / `semantic` are available. Flat fields override JSON.
- Empty file → **400**; `text` longer than `Chunking:MaxTextChars` or body
  > 20 MB → **413**.
- Response — the same `{chunks, stats}` as `/api/v1/chunk`.
- Markdown is not parsed — it is chunked as plain text (all delimiters are
  preserved).

### Semantic Strategy in the API

```json
{
  "text": "…",
  "strategy": "semantic",
  "tokenizer": "huggingface",
  "maxTokens": 512,
  "embedding": {
    "provider": "llama-cpp",
    "endpoint": "http://127.0.0.1:9932",
    "model": "embeddinggemma-2",
    "batchSize": 32,
    "maxBatchChars": 8000,
    "concurrency": 4,
    "timeoutSeconds": 120,
    "maxRetries": 3,
    "useCache": true
  },
  "semantic": {
    "percentile": 0.9,
    "minDistance": 0.02,
    "window": 1,
    "maxBytes": 20000000,
    "streamWindowBytes": 4000000,
    "streamOverlapBytes": 256000,
    "fallback": "recursive"
  }
}
```

- `fallback: "streaming"` enables windowed processing: the document is read
  in windows of `streamWindowBytes` with an overlap of
  `streamOverlapBytes`, `maxBytes` then does not limit the file size.

- The embedding server key (`TEXTCHUNKER_EMBEDDING_API_KEY`) is **not**
  accepted in the body — it is set only on the server.
- Unavailable provider → **503**; document larger than `maxBytes` with
  `fallback=error` → **413**; unknown provider/invalid parameters → **400**.
- Chunk metadata include `semanticProvider`, `semanticModel`,
  `breakpointPercentile`, and (on fallback) `semanticFallback`. Details —
  [semantic-chunking.md](semantic-chunking.md).

### Authentication of Semantic Requests

If the environment variable `TEXTCHUNKER_EMBEDDING_API_KEY` is set on the
server, requests to `POST /api/v1/chunk`, `POST /api/v1/chunk/file` and
`POST /api/v1/chunk/stream` with `strategy: "semantic"` require the header:

```http
X-API-Key: <TEXTCHUNKER_EMBEDDING_API_KEY value>
```

```bash
curl -X POST http://localhost:5080/api/v1/chunk \
  -H "Content-Type: application/json" \
  -H "X-API-Key: $TEXTCHUNKER_EMBEDDING_API_KEY" \
  -d '{"text":"…","strategy":"semantic","tokenizer":"huggingface","maxTokens":512}'
```

Missing or wrong value → **401** `application/problem+json`. If the variable
is not set, the check is disabled.

### Errors

Format — `application/problem+json` (RFC 7807):

| Code | Cause |
|---|---|
| 400 | missing/empty `text`, unknown strategy/tokenizer, invalid parameters, invalid JSON |
| 401 | for `semantic` under `/api/v1/*`, `X-API-Key` is missing or wrong (when `TEXTCHUNKER_EMBEDDING_API_KEY` is set) |
| 413 | `text` longer than `Chunking:MaxTextChars`, or body exceeds the Kestrel limit |
| 415 | unsupported file extension (not `.txt`/`.md`/`.markdown`) or non-multipart request |
| 503 | tokenizer loading or embedding provider failure (bad `tokenizerPath`, network/parsing, embedding server unreachable) |
| 500 | unexpected error |
| 499 | client disconnected |

Example:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Invalid request",
  "status": 400,
  "detail": "Unknown strategy 'bogus'. Allowed values: fixed, recursive, paragraph. …"
}
```

## Tokenizers and Cache

- The same implementations as in the CLI; for `huggingface` — exact
  `tokenCount` (including the prefix).
- A successfully loaded tokenizer is cached for the process lifetime
  (`TokenizerCache`, singleton); repeated requests do not reload the model.
- The HF model is downloaded on first use into `tokenizerCacheDir` or
  `%LOCALAPPDATA%\TextChunker\tokenizers`.

## MVP Limitations

- Authentication is limited to an optional `X-API-Key` for semantic requests
  under `/api/v1/*`.
- No persistence or background tasks.
- OpenAPI/Scalar is connected; the document is available at
  `/openapi/v1.json`.
- Request text is not logged.
