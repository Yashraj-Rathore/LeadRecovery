# ADR-0005: API contract and optimistic concurrency

- Status: Accepted
- Date: 2026-07-13

## Context

The design package includes a hand-maintained OpenAPI skeleton, while the future
ASP.NET Core implementation can generate an exact description. The domain
documentation also used database-specific row-version wording that does not
match PostgreSQL.

## Decision

`api/openapi.yaml` is the design contract until an endpoint is implemented.
Annotated ASP.NET Core endpoints become the implementation source for those
operations, and CI compares a committed generated export with the application.
An intentional contract change updates endpoint annotations, the committed
export, affected clients, and documentation together.

Lead optimistic concurrency uses an application-managed `bigint Version` that
is configured as an EF Core concurrency token and incremented on each update.
API requests and responses represent the value as an opaque base64 token named
`expectedRowVersion` or `rowVersion`. Clients compare and return the token; they
do not interpret its numeric value. A mismatch returns HTTP 409 Problem Details
with the current representation or a refetch instruction.

## Consequences

The contract cannot silently drift from implemented endpoints. Concurrency is
portable and explicit on PostgreSQL while preserving an opaque HTTP contract.
Milestone 0 keeps only the design skeleton because feature endpoints have not
been implemented.
