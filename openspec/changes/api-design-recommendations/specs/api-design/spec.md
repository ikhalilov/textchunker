# api-design

## ADDED Requirements

### Requirement: Versioned API surface

The system SHALL expose all public HTTP endpoints under the `/api/v1` prefix.

#### Scenario: Versioned health endpoint

- **WHEN** a client sends `GET /api/v1/health`
- **THEN** the API responds with `200 OK` and a JSON body containing `status` and `version`

### Requirement: Semantic API-key protection

When the `TEXTCHUNKER_EMBEDDING_API_KEY` environment variable is set, the system SHALL require a
matching `X-API-Key` header for semantic requests to `/api/v1/chunk`, `/api/v1/chunk/file`, and
`/api/v1/chunk/stream`. When the variable is not set, the system SHALL NOT require `X-API-Key`.

#### Scenario: Missing key is rejected

- **GIVEN** `TEXTCHUNKER_EMBEDDING_API_KEY` is set
- **WHEN** a client sends a semantic request to `/api/v1/chunk` without `X-API-Key`
- **THEN** the API responds with `401 Unauthorized` and an `application/problem+json` body

#### Scenario: Matching key is accepted

- **GIVEN** `TEXTCHUNKER_EMBEDDING_API_KEY` is set
- **WHEN** a client sends a semantic request to `/api/v1/chunk` with the matching `X-API-Key`
- **THEN** the API processes the request normally

#### Scenario: No configured key means no authentication

- **GIVEN** `TEXTCHUNKER_EMBEDDING_API_KEY` is not set
- **WHEN** a client sends a semantic request to `/api/v1/chunk`
- **THEN** the API processes the request without requiring `X-API-Key`

### Requirement: Consistent semantic authentication

The system SHALL enforce semantic API-key protection consistently across all semantic endpoints
through a single reusable mechanism.

#### Scenario: Missing key is rejected on every semantic endpoint

- **GIVEN** `TEXTCHUNKER_EMBEDDING_API_KEY` is set
- **WHEN** a client sends a semantic request to `/api/v1/chunk`, `/api/v1/chunk/file`, or
  `/api/v1/chunk/stream` without `X-API-Key`
- **THEN** each endpoint responds with `401 Unauthorized` and an `application/problem+json` body

### Requirement: Maintainable endpoint implementation

Cross-cutting endpoint concerns (authentication, logging, and route prefixes) SHALL be implemented
once and reused across handlers rather than duplicated in each endpoint.

#### Scenario: Route prefix is defined once

- **WHEN** an endpoint is added to the current API version
- **THEN** it is registered under one shared `/api/v1` route group

### Requirement: Interactive documentation covers versioned routes

The system SHALL publish an OpenAPI document at `/openapi/v1.json` and a Scalar UI at `/scalar`.
The document SHALL include the `/api/v1/...` routes.

#### Scenario: OpenAPI document lists versioned routes

- **WHEN** a client requests `/openapi/v1.json`
- **THEN** the document contains the versioned routes

### Requirement: RFC 7807 error responses

The system SHALL return errors as `application/problem+json` with at least `status`, `title`, and
`detail` fields.

#### Scenario: Invalid request returns ProblemDetails

- **WHEN** a client sends an invalid chunk request
- **THEN** the API responds with a 4xx status and an `application/problem+json` body

### Requirement: File upload restrictions

The system SHALL accept only `.txt`, `.md`, and `.markdown` files on the file upload endpoint and
SHALL reject other extensions with `415 Unsupported Media Type`.

#### Scenario: Unsupported extension is rejected

- **WHEN** a client uploads a file with an unsupported extension to `/api/v1/chunk/file`
- **THEN** the API responds with `415 Unsupported Media Type`
