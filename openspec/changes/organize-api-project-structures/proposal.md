# Proposal

## Why

`src/TextChunker.Api` keeps its host configuration types (`ApiOptions`,
`ApiDocsOptions` — POCOs bound from `IConfiguration`) inside `Services/`, mixed
with DI-registered services, per-request records, and exceptions. As the API
surface grows, folder placement becomes ambiguous. The previous change already
gave `*Extensions` helpers a dedicated `Extensions/` folder, but options have no
designated home.

## What Changes

- New folder `src/TextChunker.Api/Options/` holding `ApiOptions` and
  `ApiDocsOptions`; both namespaces become `TextChunker.Api.Options`; the two
  extension helpers (`ServiceExtensions.cs`, `ApiDocsExtensions.cs`) gain or
  adjust `using TextChunker.Api.Options`.
- No new `Models/` or `Entities/` folder: HTTP wire contracts stay in the
  existing `Contracts/` (the wire is the only "model" of the API); internal
  per-request records (`ChunkSession`, `ChunkResult`) and API-specific
  exceptions (`PayloadTooLargeException`, `UnsupportedMediaTypeException`,
  `ApiErrors`) stay in `Services/`.
- `docs/architecture.md` (API section) documents the four-folder layout.
- Delta spec: a new "API host project layout" requirement in
  `specs/api-design/spec.md` stating the folder-ownership rules.

No behavior change: pure file/namespace moves plus documentation. Build and all
test suites expected green.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- **`api-design`** — new requirement "API host project layout": the four
  folders of `TextChunker.Api` (`Contracts/`, `Extensions/`, `Options/`,
  `Services/`) each own a single concern, with explicit rules for where new
  host configuration types, wire contracts, and internal service state land.

## Impact

- **Code**: 2 files moved, 2 namespace changes, 2 `using` edits
  (`ServiceExtensions.cs`, `ApiDocsExtensions.cs`), one documentation update.
- **Dependency graph**: unchanged; no project references change.
- **Build**: `dotnet build TextChunker.slnx` expected clean.
- **Tests**: full suite expected green (361 tests before the change); the 63
  API tests spin up the API process and exercise the resulting DI path
  end-to-end.
- **Docs**: `docs/architecture.md`, API section.
- **Spec**: one ADDED requirement.
- **Validation**: build + full test suite + OpenAPI document spot-check
  (wire shapes must be unchanged).
