# Proposal

## Why
The TextChunker project provides a CLI and minimal‑API service for chunking large text documents. As the project evolves, it is important to adopt a stable, versioned API design that ensures backward compatibility, clear documentation, and secure, maintainable endpoints. This change proposes a set of best‑practice guidelines for API design, including versioning, documentation, error handling, and testing strategies.

## What Changes
- Add a `/api/v1` prefix to all existing endpoints for future versioning.
- Adopt an *extend‑only* approach: new endpoints are added, existing signatures are never altered. If a response format must change, a new `/v2/...` endpoint is introduced and the old one is marked `[Obsolete]`.
- Integrate Swagger/Scalar UI for interactive documentation.
- Implement RFC 7807 `ProblemDetails` for error responses.
- Validate request models using `FluentValidation` or data annotations.
- Restrict file uploads to `.txt`, `.md`, and `.markdown` extensions.
- Add comprehensive HTTP tests that assert correct status codes and `ProblemDetails` payloads.
- Export only public types from the NuGet package and generate a public‑API snapshot with `PublicApiGenerator`.
- Use `async/await` with `ConfigureAwait(false)` and stream large files via `IAsyncEnumerable<Chunk>`.
- Add optional API‑key or OAuth protection for the semantic strategy endpoints.
- Configure structured JSON logging via Serilog or Microsoft.Extensions.Logging.

## Capabilities
### New Capabilities
- **api-design**: A new capability that documents the API versioning strategy, error handling, and security practices for the TextChunker API.

### Modified Capabilities
- **api**: The existing API capability will be updated to include the `/api/v1` prefix and Swagger UI.

## Impact
- Code changes in `src/TextChunker.Api` to add routing, error handling, validation, and logging.
- Minor changes in `src/TextChunker` for public API export.
- Updates to test projects to cover new behaviors.
- Configuration changes for logging and API‑key support.

## Implementation notes (as delivered)

- Request validation is enforced by the `ChunkingEngine` backstop
  (`ChunkingOptionsValidator`, `SemanticOptions.Validate`, provider checks) and covered by the
  negative 400 tests; `FluentValidation` and data annotations are infeasible in the
  offline/trimmed-reference environment.
- Streaming keeps the existing bounded `Channel<Chunk>` (backpressure + cancellation) instead of
  `IAsyncEnumerable<Chunk>`; the HTTP contract is unchanged.
- The `PublicApiGenerator` snapshot is deferred to CI with network access (the package is
  unavailable offline).

---

**Note**: This proposal is a high‑level outline. Detailed design decisions are documented in the accompanying `design.md`.