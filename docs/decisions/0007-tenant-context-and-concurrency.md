# ADR-0007: Tenant context and concurrency

- Status: Accepted
- Date: 2026-07-13

## Context

The domain specification requires optimistic concurrency for tenant
configuration, but the original Tenant field list and reference schema omitted
a concurrency value. Authentication is deliberately deferred to Milestone 2,
while LR-0101 still requires a server-derived tenant context that fails closed.

## Decision

`Tenant.Version` is an application-managed PostgreSQL `bigint` concurrency
token. It starts at zero and is incremented by the persistence layer whenever a
tenant update is saved. EF Core includes the original value in update
predicates, so stale writes raise a concurrency exception. A future tenant
configuration API will expose this value as the same opaque base64 token pattern
defined for leads in ADR-0005.

The HTTP tenant context reads only the trusted `leadrecovery:tenant_id` claim.
Authentication middleware will issue that claim from validated tenant
membership in Milestone 2. Until then, any tenant-dependent operation without a
valid claim throws `TenantContextUnavailableException`. Request headers, query
strings, route values, and bodies do not initialize tenant authority.

## Consequences

Concurrent tenant configuration updates cannot silently overwrite each other.
The tenant context can be wired and tested before authentication without
introducing an insecure development bypass. Authentication and membership
remain outside LR-0101.
