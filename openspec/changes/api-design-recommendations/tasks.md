# Tasks

- [x] Add `/api/v1` route group in `Program.cs`.
- [x] Update existing endpoints to use the new group.
- [x] Add Swagger/Scalar UI middleware.
- [x] Implement exception handling middleware returning `ProblemDetails`.
- [ ] Add request validation using `FluentValidation` or data annotations.
- [x] Enforce file‑extension checks for `/api/v1/chunk/file`.
- [x] Add optional `X-API-Key` header validation for semantic strategy.
- [ ] Refactor chunking logic to stream via `IAsyncEnumerable<Chunk>`.
- [x] Update tests to cover new behaviors and error handling.
- [ ] Generate public‑API snapshot with `PublicApiGenerator`.
- [x] Document changes in `README.md` and `docs/`.
- [ ] Review and approve change in CI.

## Implemented in this change

The current implementation covers versioned routing, optional `X-API-Key` validation for
semantic requests, RFC 7807 error responses, file-extension checks, OpenAPI/Scalar documentation,
and updates to the repository documentation. The unchecked items remain as follow-up work.

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
