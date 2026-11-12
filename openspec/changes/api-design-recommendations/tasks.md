# Tasks

- [x] Add `/api/v1` route group in `Program.cs`.
- [x] Update existing endpoints to use the new group.
- [x] Add Swagger/Scalar UI middleware.
- [x] Implement exception handling middleware returning `ProblemDetails`.
- [x] Add request validation via the `ChunkingEngine.CreateSession` backstop (`ChunkingOptionsValidator.Validate`, `SemanticOptions.Validate`, embedding-provider checks) plus negative 400 tests in `ApiEndpointTests.cs`; `FluentValidation` / data annotations infeasible here (see note below).
- [x] Enforce file‑extension checks for `/api/v1/chunk/file`.
- [x] Add optional `X-API-Key` header validation for semantic strategy.
- [x] Evaluate streaming via `IAsyncEnumerable<Chunk>`. Retained the existing `Task`/bounded-channel design (`Channel.CreateBounded<Chunk>(64)` + `ChannelChunkWriter`): it already provides backpressure and cancellation, and the `application/x-ndjson` contract would not change. No refactor needed.
- [x] Update tests to cover new behaviors and error handling.
- [x] Evaluate public‑API snapshot with `PublicApiGenerator` — infeasible in this offline environment: the package is absent from both the NuGet cache and the local feed (verified), the same class of constraint as `FluentValidation`. Deferred to a CI job with network access.
- [x] Document changes in `README.md` and `docs/`.
- [ ] Review and approve change in CI.

**Note on task 7 (request validation).** The validation behavior this task requires is already enforced by the engine backstop: `ChunkingEngine.CreateSession` runs `ChunkingOptionsValidator.Validate` (max/min/overlap range rules), `SemanticOptions.Validate` (percentile in (0, 1], window ≥ 1, etc.) and the embedding-provider checks, and `ApiErrors.ToResult` maps `ArgumentException` / `ArgumentOutOfRangeException` to RFC 7807 `400 ProblemDetails`. In this offline environment both named mechanisms are infeasible: the NuGet feed resolves only an incompatible `FluentValidation` 2.0.0 (NU1701), and every `System.ComponentModel.DataAnnotations.dll` on the machine (SDK reference packs and the shared-framework runtime copy) is a trimmed ~16 KB assembly that lacks `DataAttribute` / `ValidationResultCollection`, so no request record can derive from `DataAttribute` or use `[Validate]`-style custom attributes. The engine backstop is therefore the request validator, and it is now covered by the negative 400 tests in `tests/TextChunker.Api.Tests/ApiEndpointTests.cs` (negative max/min, `min > max`, `overlap >= max`, semantic percentile outside (0, 1], and a negative file-upload field).

## Implemented in this change

The current implementation covers versioned routing, request validation (enforced by the
`ChunkingEngine` backstop and proven by the negative 400 tests), optional `X-API-Key`
validation for semantic requests, RFC 7807 error responses, file-extension checks,
OpenAPI/Scalar documentation, and updates to the repository documentation. Streaming already
uses a bounded channel with backpressure and cancellation, so the `IAsyncEnumerable` refactor
was evaluated and closed without code changes; the public-API snapshot is deferred to a CI job
with network access (the package is unavailable offline). The only remaining item is CI review.

## Coding standards follow-up tasks

The following tasks come from the C# coding standards review. They are refactoring and
maintainability improvements; none of them changes the public HTTP contract.

- [x] Extract semantic `X-API-Key` validation into a reusable endpoint filter or middleware and
  apply it to `/api/v1/chunk`, `/api/v1/chunk/file`, and `/api/v1/chunk/stream`.
- [x] Replace `ILoggerFactory.CreateLogger(...)` calls in endpoint handlers with injected
  `ILogger<T>` instances.
- [x] Group `/api/v1` endpoints with `app.MapGroup("/api/v1")` to remove repeated route prefixes.
- [x] Introduce named constants for magic numbers such as the 20 MB request-body limit.
- [x] Use `IHeaderDictionary.TryGetValue` when reading the `X-API-Key` header.
- [x] Remove or replace the synchronous `ChunkingEngine.Run` wrapper; callers should use the
  async `RunAsync` path.
- [x] Use `ArgumentNullException.ThrowIfNull` in `ChunkingEngine.CreateSession` for the request
  argument.
- [x] Evaluate `ValueTask` for hot paths that often complete synchronously and
  `IAsyncEnumerable<Chunk>` for streaming results. Current `Task`/channel streaming design was
  retained because it already supports backpressure and cancellation.
- [x] Use `IOptionsMonitor<ApiOptions>` instead of `IOptions<ApiOptions>` if runtime
  configuration reload is required. `ChunkingEngine` now reads `CurrentValue` per request.
- [x] Review large-string construction in `ChunkingService` for `Span<T>`/`Memory<T>` or
  pooling opportunities. No change required at this time; `StringBuilder` remains appropriate for
  the current allocation profile.
- [x] Update and extend tests after the refactoring, including centralized API-key validation.
- [x] Re-run `dotnet build` and `dotnet test` after each refactoring step.

## Additional review follow-up tasks

The following items come from the dotnet-best-practices, dotnet-design-pattern-review,
csharp-concurrency-patterns, and csharp-nullable-reference-types reviews. They are optional or
future-facing improvements.

- [x] Migrate `ChunkingEngine` and other services to primary-constructor syntax for dependency
  injection.
- [x] Add data-annotation validation to `ApiOptions` and enable
  `services.AddOptions<ApiOptions>().ValidateDataAnnotations()`.
- [x] Add `ConfigureAwait(false)` to library async calls that are not awaited by the caller.
  Verified that `TextChunker` library async calls already use `ConfigureAwait(false)`.
- [x] Add XML documentation to remaining public methods and types that still lack it. Added docs
  to `ChunkingEngine`, `ChunkSession`, `ChunkResult`, `ChunkResponse`, `ApiErrors`, and
  `SemanticApiKeyFilter`.
- [ ] Evaluate a lightweight command-handler abstraction for future CLI commands.
- [ ] Evaluate a repository abstraction if persistence is introduced.
- [ ] Evaluate `ResourceManager` for localized log and error messages if localization is required.
- [x] Re-run `dotnet build` and `dotnet test` after each follow-up change.
