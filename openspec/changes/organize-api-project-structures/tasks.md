# Tasks

## 1. Move host options into `Options/`

- [ ] 1.1 `git mv` `src/TextChunker.Api/Services/ApiOptions.cs` to
  `src/TextChunker.Api/Options/ApiOptions.cs` and `git mv`
  `src/TextChunker.Api/Services/ApiDocsOptions.cs` to
  `src/TextChunker.Api/Options/ApiDocsOptions.cs` — verify `git status` shows
  exactly these two moves.
- [ ] 1.2 Change both namespaces from `TextChunker.Api.Services` to
  `TextChunker.Api.Options` — verify `dotnet build src/TextChunker.Api` fails
  only on the two `using` edits from task 1.3.
- [ ] 1.3 `ServiceExtensions.cs`: add `using TextChunker.Api.Options;` (keep
  `using TextChunker.Api.Services;` for the three services).
  `ApiDocsExtensions.cs`: replace `using TextChunker.Api.Services;` (used only
  for `ApiDocsOptions`) with `using TextChunker.Api.Options;` — verify
  `dotnet build src/TextChunker.Api` is clean.

## 2. Document the layout

- [ ] 2.1 `docs/architecture.md` (API section): state the four-folder layout
  (`Contracts/` = HTTP wire types, `Extensions/` = static extension helpers,
  `Options/` = host configuration POCOs bound from `IConfiguration`,
  `Services/` = DI services, internal service state, exceptions) with a one-line
  rule per folder.

## 3. Record the decision in the spec

- [ ] 3.1 Delta
  `openspec/changes/organize-api-project-structures/specs/api-design/spec.md`:
  new requirement "API host project layout" with the explicit
  no-`Models/` / no-`Entities/` rule and three scenarios (host configuration
  type → `Options/`; wire contract → `Contracts/`; internal service state →
  `Services/`).
- [ ] 3.2 `openspec validate --changes --json` is green.

## 4. Verification

- [ ] 4.1 Whole-solution build green (`dotnet build TextChunker.slnx`), 0
  warnings.
- [ ] 4.2 Full test suite green (361/361; llama.cpp embedding server at
  `http://127.0.0.1:9932`, model `embeddinggemma-2`; the 63 API tests exercise
  the DI path end-to-end).
- [ ] 4.3 Diff review: only the 2 file moves, 4 namespace/`using` edits, the
  architecture doc, and the spec artifacts changed; no wire contract JSON shape
  changed (OpenAPI document diff empty).

## Workflow follow-up

- [ ] Archive the change after validation (delta merges into
  `openspec/specs/api-design/spec.md`).

## Implemented in this change

(Completed summary goes here once the change is archived.)
