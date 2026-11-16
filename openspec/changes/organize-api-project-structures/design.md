# Design

## Context

Current `TextChunker.Api` layout:

- `Contracts/` (namespace `TextChunker.Api.Contracts`) — 8 wire contracts:
  `ChunkRequest` (+ `SemanticRequest`, `EmbeddingRequest`), `ChunkResponse`,
  `FileChunkForm`, `HealthResponse`, `StrategiesResponse`,
  `TokenizersResponse`. All are `record`-based with `{ get; init; }`; their
  XML comments feed the OpenAPI schema.
- `Services/` (namespace `TextChunker.Api.Services`) — 11 files of mixed
  kinds:
  - services: `ChunkingEngine`, `TokenizerCache`, `SemanticApiKeyFilter`,
    `InMemoryTextSource`, `ChannelChunkWriter`, `CollectingChunkWriter`,
    `FileUploadReader`;
  - host configuration POCOs: `ApiOptions`, `ApiDocsOptions`;
  - internal per-request records: `ChunkSession`, `ChunkResult`;
  - exception/mapping helpers: `PayloadTooLargeException`,
    `UnsupportedMediaTypeException`, `ApiErrors`.
- `Extensions/` (namespace `TextChunker.Api.Extensions`) — `ServiceExtensions`
  (single DI entry point) and `ApiDocsExtensions`.

Sibling projects already separate host-level options from engine-level options:
`ChunkingOptions`, `SemanticOptions`, `EmbeddingOptions`
(`TextChunker.Abstractions`) and `TextSourceOptions`
(`TextChunker.Common`) are runtime options consumed by the engine; the only
host configuration POCOs in the repo are `ApiOptions` / `ApiDocsOptions`
(bound from `IConfiguration`, registered via `IOptions<T>`/`IOptionsMonitor`).

## Goals / Non-Goals

**Goals:**
- A dedicated `Options/` folder for host configuration POCOs (`ApiOptions`,
  `ApiDocsOptions`), namespace `TextChunker.Api.Options`.
- A documented, spec'd ownership rule for the `TextChunker.Api` folders:
  `Contracts/` (wire), `Extensions/` (`*Extensions`), `Options/`
  (configuration POCOs), `Services/` (services + internal state + exceptions).
- Zero behavior changes.

**Non-Goals:**
- No new `Models/` folder: `Contracts/` is already the single home for wire
  types; a second models folder would split the same concept with an ambiguous
  boundary.
- No `Entities/` folder: in .NET "entities" means a domain/persistence layer
  (EF Core). This project owns no persistence and no domain entities — those
  live in `TextChunker.Abstractions`/`TextChunker.Common`.
- No renaming of existing wire contracts (JSON property names stay stable).
- No changes to `TextChunker`, `TextChunker.Common`, or
  `TextChunker.Abstractions`.

## Decisions

**D1 — `Options/` folder, namespace `TextChunker.Api.Options`.**
`ApiOptions` and `ApiDocsOptions` move from `Services/` to `Options/`. The
name `Options` matches the framework vocabulary
(`IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>`) and the
`*Options` suffix used across the repo, so the folder is discoverable from
either direction. `ServiceExtensions.cs` keeps `using TextChunker.Api.Services;`
(still needed for the three services) and adds `using TextChunker.Api.Options;`.
`ApiDocsExtensions.cs` replaces its `using TextChunker.Api.Services;`
(used there only for `ApiDocsOptions`) with `using TextChunker.Api.Options;`.

**D2 — `Contracts/` remains the models home.**
Everything crossing the HTTP boundary already lives here with stable,
OpenAPI-fed JSON shapes. Internal per-request records stay in `Services/`:
they are service state, not wire data, and do not feed the OpenAPI schema.

**D3 — `Services/` keeps all non-wire, non-extension code.**
DI services, per-request records, API-specific exceptions, and the static
`ApiErrors` mapping are all internal host implementation. One folder with one
rule ("not wire, not extension") is sufficient while it holds 9 files; a
dedicated subfolder is only considered if it grows beyond 3+ files of a new
kind.

**D4 — Spec requirement for the layout.**
A new requirement "API host project layout" with three scenarios (host
configuration type → `Options/`; wire contract → `Contracts/`; internal service
state → `Services/`) plus the explicit no-`Models/` / no-`Entities/` rule. It
generalizes the existing "Extension helpers live in a single folder" requirement
to the whole project.

## Risks

- Missing or stale `using` after the moves — caught by the build; each task
  carries a concrete verification.
- The namespace change is confined to the two moved files plus the two
  extension files; no other project references `ApiOptions` or
  `ApiDocsOptions`.
- API tests start a separate process and verify behavior across the HTTP
  boundary; the move is invisible to them, so green results are expected.

## Migration Plan

1. `git mv` both options files into `src/TextChunker.Api/Options/`; change
   both namespaces to `TextChunker.Api.Options`.
2. Fix usings: add in `ServiceExtensions.cs`, swap in `ApiDocsExtensions.cs`.
3. Update `docs/architecture.md` (API section) with the four-folder layout.
4. Add the delta spec requirement; `openspec validate --changes` green.
5. Full build + full test suite; verify wire contracts unchanged
  (`/openapi/v1.json` diff empty).
6. Archive the change (delta merges into
  `openspec/specs/api-design/spec.md`).

**Rollback**: `git revert` of the moves — zero behavioral risk either way.

## Open Questions

None.
