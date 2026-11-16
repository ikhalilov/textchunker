# api-design

## ADDED Requirements

### Requirement: Centralized service registration

All application services of the API — chunking options binding, tokenizer cache, chunking
engine, semantic API-key filter, problem details, and OpenAPI documentation — SHALL be
registered through the single `ServiceExtensions.AddApiServices(IServiceCollection,
IConfiguration)` extension method of the `TextChunker.Api.Extensions` namespace, rather than
through individual registrations in the host startup code.

#### Scenario: Program.cs holds only host configuration and endpoint mapping

- **WHEN** `src/TextChunker.Api/Program.cs` is inspected
- **THEN** it contains exactly one service registration call (`builder.Services.AddApiServices(...)`);
  individual `AddOptions`/`AddSingleton`/`AddProblemDetails`/`AddApiDocs` calls do not appear
  in the host startup code

### Requirement: Extension helpers live in a single folder

All extension helper classes (`*Extensions`) of the `TextChunker.Api` project SHALL be located
in the `src/TextChunker.Api/Extensions/` folder.

#### Scenario: A new helper extension is added

- **WHEN** an extension helper class is added to `TextChunker.Api`
- **THEN** its source file is located in `src/TextChunker.Api/Extensions/` and its namespace
  is `TextChunker.Api.Extensions`
