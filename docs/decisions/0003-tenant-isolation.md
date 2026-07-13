# ADR-0003: Tenant isolation

- Status: Accepted
- Date: 2026-07-13

## Context

Shared-database multi-tenancy requires more than application query filters. A
globally valid primary key can still be paired with the wrong tenant unless the
database constrains the relationship.

Some provider webhooks arrive before their tenant can be resolved, so the
integration receipt ledger cannot always satisfy the same ownership rule as
business data.

## Decision

Every tenant-owned row has a non-null `TenantId`. Tenant-owned parent tables
expose a unique `(TenantId, Id)` key and tenant-owned relationships use compound
foreign keys over `(TenantId, ParentId)`. EF Core query filters are a defensive
default, not the only isolation control. Critical queries include explicit
tenant predicates and cross-tenant denial is covered by integration tests.

Browser requests never select authority by supplying a tenant ID. The active
tenant comes from the authenticated membership. Webhooks resolve tenancy from
verified provider configuration such as the destination number.

Before authentication is introduced, the HTTP tenant context recognizes only
the trusted tenant claim that the future authentication middleware will issue.
Headers, query values, route values, and request bodies are never tenant
authority. A missing, malformed, or empty claim throws and fails closed.

`ExternalEventReceipt` is an integration/system ledger rather than a browser-
visible tenant entity. Its `TenantId` is nullable until resolution and is
immutable once assigned. No tenant browser API exposes this ledger.

## Consequences

Accidental cross-tenant entity relationships are rejected by PostgreSQL.
Mappings and tests are slightly more verbose. System-ledger processing must
handle an unresolved tenant explicitly and fail closed before touching tenant
business data.
