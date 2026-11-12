# Proposal

## Why

The `TextChunker` project currently holds both the **console host** (`Cli/` + `Program.cs`) and the
shared **chunking engine** (`Sources/`, `Tokenizers/`, `Chunking/`, `Writers/`, `Embeddings/`).
The web service (`TextChunker.Api`) reaches that engine through the CLI *exe* project — a host
depending on a host — which couples the two entry points and prevents them from evolving and
publishing independently. This extracts the engine into a dedicated shared library so both the CLI
and the API reference it directly.

## What Changes

- **New project `src/TextChunker.Common`**: a net10.0 class library holding the shared engine —
  `Sources/`, `Tokenizers/`, `Chunking/` (incl. `Chunking/Semantic/`), `Writers/`, `Embeddings/`.
  Dependencies: `TextChunker.Abstractions` + `Microsoft.ML.Tokenizers` only (no Microsoft.Extensions
  / DI / Logging).
- **`src/TextChunker` (CLI)** — reduced to the host only: keep `Cli/CliRunner.cs` + `Program.cs`;
  remove the engine directories. Keep `System.CommandLine` + Microsoft.Extensions DI/Logging.
  References Abstractions + **Common**; drops its direct `Microsoft.ML.Tokenizers` reference
  (now owned only by Common).
- **`src/TextChunker.Api`** — swap the `..\TextChunker\` project reference for
  `..\TextChunker.Common\` (keep the Abstractions reference). No code changes needed because the
  `TextChunker.*` namespaces are preserved.
- **Tests** — `TextChunker.Tests`: add a `..\..\src\TextChunker.Common\` reference (engine
  types: Chunking, Embeddings, Tokenizers, Sources, Writers) and **keep** the existing
  `..\..\src\TextChunker\` reference (host type `CliRunner.Run` used by `CancellationTests.cs`).
  `TextChunker.IntegrationTests`: **unchanged** (spawn the CLI process as `TextChunker.dll`; the
  CLI reference provides that dll in the test output). `TextChunker.Api.Tests` is unchanged
  (it references the API).
- **`tools/SemanticCalibration`** — re-point `..\..\src\TextChunker\` to
  `..\..\src\TextChunker.Common\`.
- **`TextChunker.slnx`** — add the new `TextChunker.Common` project (7 → 8 projects).
- **`InternalsVisibleTo`** — the CLI currently grants `TextChunker.Tests` visibility into its
  internals; move that grant to the new **Common** project (which holds the internals the tests
  exercise). Keep on the CLI only if `CliRunner` internals are still tested.
- **`docs/architecture.md`** — update the "Project Layout" section / diagram to the 8-project layout.

**Behavior is unchanged**: this is a pure code move plus reference rewiring. The CLI command-line
contract and the HTTP API are byte-for-byte identical. `skip_specs: true` — no spec-level
requirement changes (no specs exist yet).

## Capabilities

### New Capabilities

None — no new system behavior is introduced.

### Modified Capabilities

None — no existing spec-level requirement changes. (No specs currently exist; behavior is identical,
only the project layout and internal dependency graph change.)

## Impact

- **Dependency graph**: the API no longer transitively pulls the CLI exe; the CLI no longer hosts
  the engine. Both hosts depend on `Common` + `Abstractions`.
- **Build**: 8 projects; `Common` is a publishable (`IsPackable=true`) library (can be released as
  a NuGet package for downstream consumers).
- **Code**: the engine files move wholesale; namespaces are preserved so `using` statements need
  no edits.
- **Package refs**: `Microsoft.ML.Tokenizers` moves to `Common` only; `System.CommandLine` +
  Microsoft.Extensions stay CLI-only; the API keeps its Scalar/Swashbuckle references; the CLI
  drops its direct `Microsoft.ML.Tokenizers` reference.
- **Publishing**: `dotnet publish` output paths keep their meaning; the CLI becomes a thin host exe.
- **Docs**: `docs/architecture.md` project-layout section; no CLI/API behavior docs need changes.
- **Validation**: `dotnet build TextChunker.slnx` and `dotnet test TextChunker.slnx` green;
  per-project runs still pass; API/CLI behavior verified by the existing test suite.
