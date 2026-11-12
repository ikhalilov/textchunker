# Tasks

## 1. Common project scaffold

- [x] 1.1 Create `src/TextChunker.Common/TextChunker.Common.csproj` per design D8 (net10.0 class library, `IsPackable=true`, `RootNamespace TextChunker`, `GenerateDocumentationFile=true`, `NoWarn` 1591, `Version 0.2.0`; project ref `..\TextChunker.Abstractions\TextChunker.Abstractions.csproj`; package ref `Microsoft.ML.Tokenizers 2.0.1`) — verify `dotnet build src/TextChunker.Common` succeeds.
- [x] 1.2 Add `<Project Path="src/TextChunker.Common/TextChunker.Common.csproj" />` to `TextChunker.slnx` — verify `dotnet build TextChunker.slnx` compiles the new project.

## 2. Move the engine code

- [x] 2.1 `git mv` the five engine dirs from `src/TextChunker/` to `src/TextChunker.Common/`: `Sources`, `Tokenizers`, `Chunking`, `Writers`, `Embeddings` — verify all five dirs are under `src/TextChunker.Common` and are gone from `src/TextChunker`.
- [x] 2.2 Confirm the moved files keep their `TextChunker.*` namespaces (declarations are folder-independent) — verify the set of `namespace TextChunker.` declarations in Common equals the pre-move set and no `using` edit is needed.

## 3. Shrink the CLI project (host only)

- [x] 3.1 Rewrite `src/TextChunker/TextChunker.csproj` — remove the dead `Microsoft.Extensions.DependencyInjection/Logging/Logging.Console` refs and the `Microsoft.ML.Tokenizers` ref (leave only `System.CommandLine`); keep the existing `..\TextChunker.Abstractions\TextChunker.Abstractions.csproj` ref; add `..\TextChunker.Common\TextChunker.Common.csproj`; remove `<InternalsVisibleTo Include="TextChunker.Tests" />` — verify `dotnet build src/TextChunker` succeeds and the csproj holds only `System.CommandLine` + Abstractions + Common.
- [x] 3.2 Confirm the CLI is a thin host — verify `src/TextChunker` contains only `Cli/`, `Program.cs` and `TextChunker.csproj` (engine dirs removed) and `dotnet publish src/TextChunker -c Debug` produces a host exe.

## 4. API reference swap

- [x] 4.1 In `src/TextChunker.Api/TextChunker.Api.csproj` swap `..\TextChunker\TextChunker.csproj` for `..\TextChunker.Common\TextChunker.Common.csproj` (keep `..\TextChunker.Abstractions\TextChunker.Abstractions.csproj`) — verify the API csproj references only Common + Abstractions.
- [x] 4.2 Confirm the API builds clean and no longer transitively pulls the CLI host — verify `dotnet build src/TextChunker.Api` succeeds and `dotnet list package ... --include-transitive` shows no `System.CommandLine`/`Microsoft.Extensions.*` from the old CLI path.
- [x] 4.3 Run the API tests to confirm behavior is unchanged — verify `dotnet test tests/TextChunker.Api.Tests` is green (57 passed).

## 5. Test project references + engine tests

- [x] 5.1 In `tests/TextChunker.Tests/TextChunker.Tests.csproj` add `..\..\src\TextChunker.Common\TextChunker.Common.csproj` (keep `..\..\src\TextChunker\TextChunker.csproj` for `CliRunner` and `..\..\src\TextChunker.Abstractions\...`) — verify the project references Abstractions + CLI + Common.
- [x] 5.2 Confirm `TextChunker.IntegrationTests` and `TextChunker.Api.Tests` csproj files are unchanged (they keep the CLI reference / reference the API as before) — verify no accidental edits.
- [x] 5.3 Run the unit tests (engine + host `CancellationTests`) — verify `dotnet test tests/TextChunker.Tests` is green (263 passed).
- [x] 5.4 Run the process-based CLI integration tests — verify `dotnet test tests/TextChunker.IntegrationTests` is green (35 passed; semantic cases require a running llama.cpp per `docs/semantic-chunking.md`, non-semantic ones need no server).

## 6. Calibration tool

- [x] 6.1 In `tools/SemanticCalibration/SemanticCalibration.csproj` swap `..\..\src\TextChunker\TextChunker.csproj` for `..\..\src\TextChunker.Common\TextChunker.Common.csproj` — verify `dotnet build tools/SemanticCalibration` succeeds.

## 7. Documentation

- [x] 7.1 Update `docs/architecture.md` "Project Layout" section/diagram to the 8-project layout (add `TextChunker.Common`; show CLI and API both referencing it) — verify the layout section names `Common` and matches the new `TextChunker.slnx`.

## 8. Full integration verification

- [x] 8.1 Build the entire solution — verify `dotnet build TextChunker.slnx` is green with no warnings about missing refs.
- [x] 8.2 Run the whole test suite — verify `dotnet test TextChunker.slnx` is green (unit + API; integration as in 5.4).
- [x] 8.3 Confirm the dependency graph — verify `dotnet list project TextChunker.slnx --include-transitive` shows API→Common (not API→CLI) and CLI→Common; the graph has no host→host edges.

## Workflow follow-up

- [x] Review the diff (engine move + csproj rewiring) for accidental source edits.
- [x] Archive the change after the project's review requirements are satisfied.

## Implemented in this change

The refactor lands as: engine moved wholesale to a new `TextChunker.Common` class library (5 dirs, namespaces preserved, zero `using` edits), the CLI reduced to a thin host, the API re-pointed to Common (no longer transitively pulling the CLI exe), tests re-wired to reference Common, the calibration tool re-pointed, and `TextChunker.slnx` / `docs/architecture.md` updated to the 8-project layout. `InternalsVisibleTo` for `TextChunker.Tests` moved to Common. Build and the full test suite (355 passed, 0 failed) are green.

**Coding standards note (D7):** `<InternalsVisibleTo Include="TextChunker.Tests" />` was added to `TextChunker.Common.csproj` (required for unit tests that exercise engine internals: `LlamaCppEmbeddingProvider.EmbeddingsUrlForTests`, `HuggingFaceTokenizer` internal constructor, `SemanticPlanner.Percentile`), and removed from the CLI (which only exposes the public `CliRunner.Run`).
