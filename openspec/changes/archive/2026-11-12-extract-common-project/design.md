# Design

## Context

Current dependency graph (see proposal.md for the motivation):

- `src/TextChunker` (Exe, CLI host) contains both the **host** (`Cli/` + `Program.cs`) and the
  shared **engine** (`Sources/`, `Tokenizers/`, `Chunking/` incl. `Semantic/`, `Writers/`,
  `Embeddings/`).
- `src/TextChunker.Api` (Web) reaches the engine via a project reference to the CLI **exe** — a
  host depending on a host.
- The interfaces `ITextSource`, `ITokenizer` and the embedding/exception types already live in
  `TextChunker.Abstractions`; only the **concrete engine implementations** sit in `src/TextChunker`.

Verified constraints that shape the approach:

- The engine uses only `TextChunker.Abstractions` + `Microsoft.ML.Tokenizers`; there is **no**
  Microsoft.Extensions usage anywhere in it.
- `Microsoft.ML.Tokenizers` is used only by `HuggingFaceTokenizer` (engine); the CLI host
  (`Cli/` + `Program.cs`) does not use it directly (it calls `TokenizerFactory.Create`).
- The CLI source emits errors via `Console.Error.WriteLine`; the CLI csproj's
  `Microsoft.Extensions.DependencyInjection/Logging/Logging.Console` references are **unused**
  (dead) — no `ILogger`/`Microsoft.Extensions` anywhere in the CLI source.
- `CliRunner.Run` is `public static int Run(...)` in `TextChunker.Cli`; it is exercised
  in-process by `tests/TextChunker.Tests/CancellationTests.cs`.
- Every engine file has an explicit `namespace TextChunker.*` declaration (namespace is **not**
  folder-derived), so moving files to a new folder keeps namespaces unchanged.

## Goals / Non-Goals

**Goals:**
- Create `src/TextChunker.Common` (net10.0 class library) holding the engine; the CLI and the API
  reference it directly.
- Reduce the CLI to a thin host (`Cli/` + `Program.cs` only).
- Make the API a web service with **no** reference to the CLI exe.
- Preserve all existing namespaces (`TextChunker.*`) so no `using` statement needs editing.
- Keep behavior byte-for-byte identical (CLI contract and HTTP API unchanged).

**Non-Goals:**
- No behavioral changes to chunking strategies, tokenizers, embeddings, or the HTTP API.
- No renames of namespaces or types.
- No changes to the `TextChunker.Abstractions` contract project.
- No new CI workflow or packaging infrastructure.

## Decisions

**D1 — Project name and location.** `src/TextChunker.Common`, a class library (user-confirmed).
Idiomatic, neutral .NET name for shared code and avoids the `Core`/ASP.NET-Core collision.
*Alternative:* `TextChunker.Engine` is the most semantically precise name (CLI/API are "hosts"
driving one "chunking engine"); kept as a viable option, `Common` chosen per the user's preference.
`TextChunker.Core` rejected (collides with ASP.NET **Core**); `Base`/`Shared` are weaker synonyms.

**D2 — Move scope: the five engine directories, wholesale.** `Sources/`, `Tokenizers/`,
`Chunking/` (incl. `Chunking/Semantic/`), `Writers/`, `Embeddings/` move into Common. The only
non-engine parts of the CLI project are `Cli/` + `Program.cs`, which stay.

**D3 — Preserve namespaces; `RootNamespace = TextChunker`.** Common's `RootNamespace` is `TextChunker`
so moved code keeps `TextChunker.Sources`, `TextChunker.Chunking`, etc. (explicit `namespace`
declarations are folder-independent). *Rationale:* zero `using` edits across CLI/API/tests/calibration
→ minimal diff, zero behavioral risk. *Alternative:* `TextChunker.Common.*` namespaces — rejected
(would force `using` edits in every project).

**D4 — CLI shrinks to the host and drops dead refs.** `src/TextChunker.csproj`: removes the five
engine dirs and keeps `System.CommandLine`; **drops** the unused `Microsoft.Extensions.DependencyInjection/Logging/Logging.Console`
refs (verified dead) and the direct `Microsoft.ML.Tokenizers` ref (now owned by Common, reached
transitively). *Rationale:* "CLI is console-only" — no dead deps.

**D5 — API reference swap.** `TextChunker.Api.csproj`: `..\TextChunker\` → `..\TextChunker.Common\`
and keep `..\TextChunker.Abstractions\`. No source changes (namespaces preserved).
*Effect:* the API no longer transitively pulls the CLI host — a cleaner dependency graph.

**D6 — Test project references.**
- `TextChunker.Tests`: **add** `..\..\src\TextChunker.Common\` (engine types) and **keep** the
  `..\..\src\TextChunker\` ref (host `CliRunner.Run` used by `CancellationTests.cs`).
- `TextChunker.IntegrationTests`: **unchanged** — it spawns `dotnet TextChunker.dll` (a path
  string, process-based); it needs the CLI reference so `TextChunker.dll` is copied into the test
  output. It uses no engine types directly.
- `TextChunker.Api.Tests`: **unchanged** (references the API).

**D7 — `InternalsVisibleTo`.** The grant moves to Common
(`<InternalsVisibleTo Include="TextChunker.Tests" />`) because the unit tests exercise engine
*internals*. It is **removed** from the CLI (the only test touching it is `CancellationTests`,
which uses the public `CliRunner.Run` — no internals).

**D8 — `Common.csproj` shape.** `Microsoft.NET.Sdk`; net10.0; `ImplicitUsings`/`Nullable` enable;
`IsPackable=true` (publishable NuGet library); `GenerateDocumentationFile=true`; `NoWarn` 1591;
`Version 0.2.0`; `RootNamespace TextChunker`. Project ref: `..\TextChunker.Abstractions`.
Package ref: `Microsoft.ML.Tokenizers 2.0.1`.

**D9 — Calibration tool + solution.** `tools/SemanticCalibration.csproj`:
`..\..\src\TextChunker\` → `..\..\src\TextChunker.Common\` (it uses only engine/Abstractions
namespaces). `TextChunker.slnx`: add
`<Project Path="src/TextChunker.Common/TextChunker.Common.csproj" />` (7 → 8 projects).

**D10 — Documentation.** Update the "Project Layout" section/diagram in `docs/architecture.md`
to the 8-project layout. No CLI/API behavior docs need changes.

## Risks / Trade-offs

- [A test uses an internal CLI host member not yet caught] → only `CancellationTests` touches
  `TextChunker.Cli`, and only the public `CliRunner.Run`; integration tests are process-based.
  Build + the full test suite immediately after the move catch any miss.
- [Calibration/API secretly use a CLI host type] → verified: both use only `Abstractions` + engine
  namespaces (`Chunking.Semantic`, `Embeddings`, `Tokenizers`), no `Cli`.
- [Integration tests lose `TextChunker.dll` in output] → their CLI reference is unchanged, so the
  dll is still copied to the test base directory.
- [Transitive `Microsoft.Extensions`/`System.CommandLine` leak into the API] → eliminated by the
  reference swap; confirm with
  `dotnet list package TextChunker.Api --include-transitive`.
- [The "dead" Microsoft.Extensions refs were actually used] → re-grepped the whole CLI source
  (nothing; the CLI logs via `Console.Error.WriteLine`). Trivially reversible if wrong (re-add refs).
- Trade-off: `TextChunker.Tests` now references both the CLI and Common, so the unit-test build
  pulls both. Matches the current state; low cost.

## Migration Plan

1. `mkdir src/TextChunker.Common`; create `TextChunker.Common.csproj` per **D8**.
2. Move the five engine dirs into Common (`git mv src/TextChunker/{Sources,Tokenizers,Chunking,Writers,Embeddings} src/TextChunker.Common/`).
3. Edit `src/TextChunker.csproj` (CLI) per **D4/D7**: drop engine dirs, dead Microsoft.Extensions +
   `Microsoft.ML.Tokenizers` refs; keep Abstractions; add Common; remove `InternalsVisibleTo`.
4. `TextChunker.Api.csproj`: swap ref per **D5**.
5. `TextChunker.Tests.csproj`: add the Common ref (keep the CLI ref) per **D6**.
6. `tools/SemanticCalibration.csproj`: swap ref per **D9**.
7. `TextChunker.slnx`: add the Common project per **D9**.
8. Update `docs/architecture.md` layout per **D10**.
9. `dotnet build TextChunker.slnx`, then `dotnet test TextChunker.slnx` — both green.
10. (Optional) `dotnet publish src/TextChunker` to confirm the CLI is now a thin host exe;
    `dotnet list package TextChunker.Api --include-transitive` to confirm the clean graph.

**Rollback:** the change lands in one or a few commits — revert with `git revert`. Low risk: purely
structural, no data/model/API changes, behavior verified by the existing suite.

## Open Questions

None that block the task breakdown. *(Optional, later: publish `TextChunker.Common` as a real
NuGet package — not needed for this refactor.)*
