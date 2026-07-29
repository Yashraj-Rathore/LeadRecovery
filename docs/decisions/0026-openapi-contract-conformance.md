# ADR-0026: Committed OpenAPI contract and executable route conformance

- Status: Accepted; amends ADR-0005 API provenance
- Date: 2026-07-29
- Decision owners: LeadRecovery engineering

## Context

ADR-0005 anticipated framework annotations as the implementation source and a
generated OpenAPI export comparison. The implemented Minimal API remained
contract-first instead: `api/openapi.yaml` is reviewed and consumed directly,
but no generated schema exporter was introduced. Leaving that difference
implicit would make route drift possible and misstate the CI evidence.

## Decision

1. `api/openapi.yaml` remains the authoritative versioned external contract.
2. Every operation has one stable `operationId`, a same-origin relative server,
   and passes the pinned Redocly rules without warnings.
3. A PostgreSQL/API-host integration test enumerates all implemented
   `/api/v1` route templates and HTTP methods, normalizes framework route
   constraints and route-group terminal slashes, parses the committed contract,
   and requires exact set equality.
   An undocumented endpoint and a documented but missing endpoint both fail CI.
4. Request/response schema changes remain explicit contract review: update the
   DTO/endpoint behavior, OpenAPI schema, clients, tests, and documentation in
   one change. The repository does not claim generated schema equivalence.
5. A future generated exporter may replace the committed-schema workflow only
   through another accepted decision and equivalent breaking-change controls.

## Consequences

- Route and method drift is executable evidence rather than a documentation
  convention, without adding runtime OpenAPI packages to the production API.
- Stable operation IDs support later client generation and change review.
- Schema fidelity still depends on focused endpoint tests and human contract
  review; the route conformance test deliberately does not pretend to validate
  every JSON field.
