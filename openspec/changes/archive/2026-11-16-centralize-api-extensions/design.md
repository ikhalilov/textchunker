# Design

## Context

Before the change:

- `src/TextChunker.Api/Program.cs` performs **six** individual service registrations:
  `AddOptions<ApiOptions>()` + `Bind("Chunking")` + `ValidateDataAnnotations()` +
  `ValidateOnStart()`; `AddSingleton<TokenizerCache>()`; `AddSingleton<ChunkingEngine>()`;
  `AddSingleton<SemanticApiKeyFilter>()`; `AddProblemDetails()`;
  `AddApiDocs(configuration)` (from `Services/ApiDocsExtensions`).
- Extension helpers are split across folders: `ServiceExtensions.cs` in `Extensions/`
  (namespace `TextChunker.Api.Extensions`) and `ApiDocsExtensions.cs` in `Services/`
  (namespace `TextChunker.Api.Services`).
- No convention existed for where extension helper classes of the API project should live.

Verified constraints that shape the approach:

- Both `*Extensions` classes are `static` with no interdependencies other than
  `ApiDocsOptions` (a `Services/` configuration POCO) and framework/Scalar packages.
- C# namespaces are declared explicitly and are **folder-independent** (the same property relied
  upon in the earlier `extract-common-project` change), so a folder move requires exactly one
  `namespace` edit plus one `using` to keep `ApiDocsOptions` visible.
- References after the move: `ServiceExtensions` (same new namespace) and `Program.cs`
  (`MapApiDocs()` extension call) — both resolve through `TextChunker.Api.Extensions`,
  which `Program.cs` already imports.

## Goals / Non-Goals

**Goals:**
- A single service registration entry point for the API:
  `ServiceExtensions.AddApiServices(IServiceCollection, IConfiguration)`.
- All `*Extensions` helper classes of `TextChunker.Api` in one folder:
  `src/TextChunker.Api/Extensions/` (now: `ServiceExtensions` + `ApiDocsExtensions`).
- `Program.cs` as a thin host: Kestrel limits, middleware, and endpoint mapping only.
- Record the actions in the OpenSpec `api-design` spec.

**Non-Goals:**
- No changes to behavior, the HTTP contract, or configuration sections (`Chunking`, `ApiDocs`).
- `ApiDocsOptions` does not move — it is a configuration type, not an extension helper.
- No changes to `TextChunker` (CLI), `TextChunker.Common`, `TextChunker.Abstractions`, or tests.
- No changes to `ApiDocsOptions`/`ApiDocs` routing behavior (the `Enabled=false` no-op and the
  root redirect are preserved verbatim).

## Decisions

**D1 — Single entry point `AddApiServices` inside `ServiceExtensions`.**
The six `Program.cs` calls are replaced one-to-one by the single extension method call. Same
sections, same lifetimes, same validation — `Program.cs` keeps the `using` for
`TextChunker.Api.Extensions`. *Alternative:* keep `AddApiDocs` as a second entry point in
`Program.cs` — rejected: the request was to consolidate *all* service registrations (and all
`*Extensions` classes) into the `Extensions/` home.

**D2 — `ApiDocsExtensions` moved to `Extensions/` with namespace
`TextChunker.Api.Extensions`.**
The class body is unchanged except `using TextChunker.Api.Services` (for `ApiDocsOptions`).
`public static` and `const DocumentName = "v1"` are preserved, so the OpenAPI
`/openapi/v1.json` + Scalar routes are identical.

**D3 — Spec delta documents internal structure, not endpoints.**
New requirement: centralized service registration via `AddApiServices`, with `Program.cs`
free of individual registrations; all `*Extensions` helper classes in `Extensions/`.
Modeled on the existing "Maintainable endpoint implementation" requirement, which already
captures a cross-cutting implementation concern in the same spec.

## Risks

- A missing `using` after the namespace move (missed `ApiDocsOptions` reference) → build
  catches it; verified green.
- Test harness expectations: API tests spawn a separate API process, so in-process DI
  layout has no effect on them; they validate the resulting behavior end-to-end (63/63 green).
- Over-centralization: `Program.cs` is the *only* consumer of `AddApiServices`; a second host
  needing different wiring could compose the sub-calls directly — the sub-calls
  (`AddApiDocs`) remain public, so this is not a problem.

## Migration Plan

The implementation already landed in this session (code + build + tests green). Remaining
workflow steps:

1. Write planning artifacts (`proposal.md`, `design.md`, `tasks.md`, delta spec).
2. `openspec validate --change centralize-api-extensions` — green.
3. `openspec archive --change centralize-api-extensions` — moves the change to
   `openspec/changes/archive/` and merges the delta requirement into
   `openspec/specs/api-design/spec.md`.

**Rollback:** single small commit set — revert with `git revert`. Zero behavioral risk.

## Open Questions

None.
