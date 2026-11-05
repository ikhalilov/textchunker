# Design

## Context
The current TextChunker API exposes endpoints such as `/api/chunk` and `/api/chunk/file`. It uses minimal‑API routing without explicit versioning, and error handling is minimal. To support future growth and external consumers, we need a stable, documented, and secure API surface.

## Goals / Non-Goals
**Goals:**
- Provide a versioned API surface that can evolve without breaking existing clients.
- Ensure all endpoints are documented and testable.
- Standardize error responses using RFC 7807 `ProblemDetails`.
- Enforce request validation and restrict file uploads.
- Add structured logging and optional authentication for sensitive endpoints.
- Maintain a clean public API surface for the NuGet package.

**Non-Goals:**
- Introducing new business logic or data models beyond API surface changes.
- Implementing a full authentication system; only optional API‑key support is added.

## Decisions
1. **Routing Prefix** – Add `/api/v1` as a route group. Future versions can use `/api/v2`, etc.
2. **Extend‑Only API** – Existing method signatures are never changed. New functionality is added as new endpoints or overloads. Deprecated endpoints are marked `[Obsolete]` and will be removed in a major release.
3. **Swagger/Scalar UI** – Integrate `Swashbuckle.AspNetCore` to expose OpenAPI JSON and a UI at `/scalar`. This is the standard approach for minimal‑API services.
4. **Error Handling** – Use `ProblemDetails` via `UseExceptionHandler` middleware. All errors will return a JSON payload with `status`, `title`, `detail`, and `type`.
5. **Validation** – Use `FluentValidation` for request models. Data annotations are also acceptable for simple cases.
6. **File Uploads** – Accept only `.txt`, `.md`, and `.markdown`. Validate the extension before processing.
7. **Logging** – Configure `Microsoft.Extensions.Logging` with JSON output. Use Serilog for structured logs if desired.
8. **API‑Key Support** – Add a simple header `X-API-Key`. When `TEXTCHUNKER_EMBEDDING_API_KEY` is configured, semantic requests to `/api/v1/chunk`, `/api/v1/chunk/file`, and `/api/v1/chunk/stream` must provide the matching header; otherwise the API returns 401. If the variable is not set, no key is required.
9. **Async Streaming** – Use `IAsyncEnumerable<Chunk>` for large file responses to avoid loading the entire payload into memory.
10. **Public API Export** – Keep the NuGet package minimal: only public types (`Chunk`, `ChunkingOptions`, `IChunker`, etc.). Generate a public‑API snapshot with `PublicApiGenerator` and store it in `src/TextChunker/Api/PublicApi.verified.txt`.

## Follow-up refactoring (coding standards)

The C# coding-standards review identified maintainability improvements that do not change the
HTTP contract:

1. **Centralized authentication** – Implement the semantic `X-API-Key` check once as an endpoint
   filter or middleware instead of duplicating it in `/api/v1/chunk`, `/api/v1/chunk/file`, and
   `/api/v1/chunk/stream`.
2. **Route grouping** – Use `app.MapGroup("/api/v1")` so endpoint definitions do not repeat the
   version prefix.
3. **Typed logging** – Inject `ILogger<T>` instead of calling `ILoggerFactory.CreateLogger(...)`
   inside endpoint handlers.
4. **Named constants** – Replace magic numbers such as the 20 MB request-body limit with named
   constants.
5. **Header access** – Use `IHeaderDictionary.TryGetValue` when reading `X-API-Key`.
6. **Async-only engine API** – Remove or replace the synchronous `ChunkingEngine.Run` wrapper; keep
   the async `RunAsync` path as the only entry point.
7. **Argument guards** – Use `ArgumentNullException.ThrowIfNull` for null request arguments.
8. **Streaming and hot paths** – Evaluate `ValueTask` where calls often complete synchronously and
   `IAsyncEnumerable<Chunk>` for streaming.
9. **Configuration reload** – Switch to `IOptionsMonitor<ApiOptions>` if runtime reload is needed.
10. **Allocation review** – Check large-string construction in `ChunkingService` for
    `Span<T>`/`Memory<T>` or pooling opportunities.

These items are tracked as tasks in `tasks.md` and should be completed incrementally with tests
and a full build after each step.

## Additional review follow-up

The dotnet-best-practices, dotnet-design-pattern-review, csharp-concurrency-patterns, and
csharp-nullable-reference-types reviews added the following optional or future-facing items.
Items 1–4 are implemented; items 5–7 remain optional future work.

1. **Primary constructors** – Use primary-constructor syntax for `ChunkingEngine` and other
   dependency-injected services.
2. **Options validation** – Add data-annotation validation to `ApiOptions` and enable
   `ValidateDataAnnotations()`.
3. **ConfigureAwait** – Add `ConfigureAwait(false)` to library async calls that are not awaited by
   the caller.
4. **XML documentation** – Fill remaining gaps in public XML documentation.
5. **Command handlers** – Evaluate a lightweight command-handler abstraction for future CLI
   commands.
6. **Repository abstraction** – Evaluate a repository abstraction if persistence is introduced.
7. **Localization** – Evaluate `ResourceManager` for localized log and error messages.

These items are tracked in `tasks.md` under “Additional review follow-up tasks”.

## Risks / Trade-offs
- **Performance vs. Simplicity** – Streaming responses improve memory usage but increase complexity in client code.
- **Security vs. Convenience** – Adding API‑key support adds a security layer but requires users to manage keys.
- **Versioning Overhead** – Adding a version prefix increases the number of routes but preserves backward compatibility.
- **Validation Library Choice** – Using `FluentValidation` adds a dependency but offers richer validation logic.

## Migration Plan
1. Add `/api/v1` route group in `Program.cs`.
2. Update existing endpoints to use the new group.
3. Add Swagger middleware and ensure `/scalar` serves the UI.
4. Implement exception handling middleware.
5. Add request validation for all models.
6. Implement file‑extension checks.
7. Add optional API‑key header validation.
8. Refactor chunking logic to use `IAsyncEnumerable<Chunk>` for streaming.
9. Update tests to cover new behaviors.
10. Generate public‑API snapshot.

## Open Questions
- Should we expose a `/api/v2` endpoint for the semantic strategy from the start, or keep it behind `/api/v1` until a major release?
- Resolved: when `TEXTCHUNKER_EMBEDDING_API_KEY` is set, a missing or mismatched `X-API-Key` is rejected with 401; when it is not set, semantic requests are allowed without a key. 
- Should we add rate‑limiting or other protective measures for the semantic endpoint?

---

**Note**: Detailed implementation is left to the development team; this design document focuses on the architectural decisions and trade‑offs.