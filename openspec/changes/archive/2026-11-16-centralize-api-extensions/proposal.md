# Proposal

## Why

The API host (`Program.cs`) currently mixes **host configuration** (Kestrel limits, middleware,
endpoint mapping) with **service registration** (options binding, singletons, problem details,
OpenAPI docs), and the two `*Extensions` helper classes of the project live in *different*
folders: `ServiceExtensions` in `Extensions/`, `ApiDocsExtensions` in `Services/`. Consolidating
all service registration behind a single entry point in a single folder makes the API wiring
discoverable in one place and keeps `Program.cs` a thin host.

## What Changes

- **`src/TextChunker.Api/Extensions/ServiceExtensions.cs`** — new
  `AddApiServices(this IServiceCollection, IConfiguration)` method that registers:
  - `ApiOptions` options binding (`Chunking` section, `ValidateDataAnnotations()`,
    `ValidateOnStart()`);
  - singletons `TokenizerCache`, `ChunkingEngine`, `SemanticApiKeyFilter`;
  - RFC 7807 problem details (`AddProblemDetails()`);
  - OpenAPI + Scalar docs (`AddApiDocs(configuration)`).
- **`src/TextChunker.Api/Program.cs`** — the six individual `builder.Services.*` calls are
  replaced by the single call `builder.Services.AddApiServices(builder.Configuration);`.
- **`src/TextChunker.Api/Services/ApiDocsExtensions.cs` → `src/TextChunker.Api/Extensions/ApiDocsExtensions.cs`** —
  the last `*Extensions` class is moved into `Extensions/`; its namespace switches from
  `TextChunker.Api.Services` to `TextChunker.Api.Extensions` (one additional `using` for
  `ApiDocsOptions`).
- **`ApiDocsOptions` stays in `Services/`** — it is a configuration POCO, not an extension helper.
- **Spec** — a delta requirement *"Centralized service registration"* is added to
  `specs/api-design/spec.md` in this change.

**Behavior is unchanged**: identical service types, lifetimes, and validation are registered;
`dotnet build src/TextChunker.Api` is clean; `dotnet test tests/TextChunker.Api.Tests` is
63/63 green (against a running llama.cpp embedding server at `http://127.0.0.1:9932`).

## Capabilities

### New Capabilities

None — no new system behavior is introduced.

### Modified Capabilities

- **`api-design`** — new requirement: service registration SHALL be consolidated behind the single
  `ServiceExtensions.AddApiServices` entry point, and all `*Extensions` helper classes of
  `TextChunker.Api` SHALL live in the `Extensions/` folder. No changes to any HTTP endpoint
  contract, error format, or behavior.

## Impact

- **Dependency graph**: unchanged — no project references or namespace declarations of consumed
  types change; only the layout inside `TextChunker.Api` changes.
- **Build**: `dotnet build src/TextChunker.Api` green, 0 warnings; whole-solution build unaffected.
- **Tests**: API suite 63/63 green (server-spawned tests verify the full request/response cycle
  through the new wiring).
- **Package refs**: none added/removed.
- **Publishing**: unchanged.
- **Docs**: delta requirement merged into `openspec/specs/api-design/spec.md` on archive.
- **Validation**: build + full API test suite green.
