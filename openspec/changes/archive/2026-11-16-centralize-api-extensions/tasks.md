# Tasks

## 1. Centralize DI registration in `ServiceExtensions`

- [x] 1.1 Add `AddApiServices(this IServiceCollection services, IConfiguration configuration)` to
  `src/TextChunker.Api/Extensions/ServiceExtensions.cs`: options binding
  `Chunking` → `ApiOptions` (with `ValidateDataAnnotations()`/`ValidateOnStart()`),
  singletons `TokenizerCache`, `ChunkingEngine`, `SemanticApiKeyFilter`,
  `AddProblemDetails()`, `AddApiDocs(configuration)` — verify `dotnet build src/TextChunker.Api`
  succeeds.
- [x] 1.2 Replace the six individual `builder.Services.*` calls in
  `src/TextChunker.Api/Program.cs` with the single `builder.Services.AddApiServices(builder.Configuration);`
  — verify the API builds and the full API test suite passes (63/63).

## 2. Gather all `*Extensions` classes into `Extensions/`

- [x] 2.1 `git mv` `src/TextChunker.Api/Services/ApiDocsExtensions.cs` to
  `src/TextChunker.Api/Extensions/ApiDocsExtensions.cs` — verify the only `*Extensions`
  classes of the project are now in `src/TextChunker.Api/Extensions/`.
- [x] 2.2 Switch its namespace from `TextChunker.Api.Services` to `TextChunker.Api.Extensions`
  and add `using TextChunker.Api.Services` (for `ApiDocsOptions`) — verify the build is clean
  with no dangling references.

## 3. Record the actions in the spec

- [x] 3.1 Delta spec
  `openspec/changes/centralize-api-extensions/specs/api-design/spec.md` with new requirement
  "Centralized service registration" (single `AddApiServices` entry point; all `*Extensions`
  helper classes in `Extensions/`) and one scenario.
- [x] 3.2 `openspec validate --change centralize-api-extensions` is green.

## 4. Verification

- [x] 4.1 Whole-solution build green (`dotnet build TextChunker.slnx`).
- [x] 4.2 API test suite green (63/63; llama.cpp embedding server at `http://127.0.0.1:9932`,
  model `embeddinggemma-2` — semantic cases exercise the new DI path end-to-end).
- [x] 4.3 Diff review: only `Program.cs` (call replacement),
  `ServiceExtensions.cs` (new method + usings), the `ApiDocsExtensions` move, and the spec
  artifacts changed; no accidental behavior edits.

## Workflow follow-up

- [x] Archive the change after validation (delta merges into
  `openspec/specs/api-design/spec.md`).

## Implemented in this change

The API now registers all of its services through a single entry point,
`ServiceExtensions.AddApiServices(IServiceCollection, IConfiguration)`, and the entire `*Extensions`
surface of `TextChunker.Api` lives in `src/TextChunker.Api/Extensions/`
(`ServiceExtensions` + `ApiDocsExtensions`, namespace `TextChunker.Api.Extensions`).
`Program.cs` is a thin host: `AddApiServices` plus Kestrel limits, middleware, and
`/api/v1` endpoint mapping. Build and the API suite (63/63) are green; no behavior changed.
