# api-design

## ADDED Requirements

### Requirement: API host project folder layout

The `TextChunker.Api` project SHALL keep four dedicated source folders, each
owning one concern: `Contracts/` — HTTP wire types (request/response and
multipart payloads that feed the OpenAPI schema); `Extensions/` — static
extension helpers; `Options/` — host configuration POCOs bound from
`IConfiguration`; `Services/` — DI services, internal per-request state records,
and API-specific exceptions.

#### Scenario: Host configuration type

- **WHEN** a new type bound from `IConfiguration` is added to `TextChunker.Api`
- **THEN** it is located in `src/TextChunker.Api/Options/`, carries the
  `*Options` suffix, and its namespace is `TextChunker.Api.Options`

#### Scenario: Wire contract

- **WHEN** a new HTTP request or response shape is added to `TextChunker.Api`
- **THEN** it is located in `src/TextChunker.Api/Contracts/`, and existing
  wire contract JSON property names are not changed

#### Scenario: Internal service state

- **WHEN** a per-request record describing service state is added (e.g. a
  session or result holder)
- **THEN** it is located in `src/TextChunker.Api/Services/` and does not feed
  the OpenAPI schema

### Requirement: No models or entities folders

The `TextChunker.Api` project SHALL NOT introduce `Models/` or `Entities/`
folders for wire contracts or domain types. Wire contracts live in
`Contracts/`; domain types live in `TextChunker.Abstractions` and
`TextChunker.Common`.

#### Scenario: Proposed new models folder

- **WHEN** a new API type that crosses the HTTP boundary is being placed
- **THEN** it lands in `Contracts/`, and a `Models/` or `Entities/` folder
  is not created in `src/TextChunker.Api/`
