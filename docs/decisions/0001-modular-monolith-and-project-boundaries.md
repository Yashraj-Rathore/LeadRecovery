# ADR-0001: Modular monolith and project boundaries

- Status: Accepted
- Date: 2026-07-13

## Context

The product needs independently runnable API, worker, and browser processes,
but its business domains are not mature enough to justify network boundaries,
independent databases, or distributed transactions.

## Decision

Build one modular-monolith solution and one PostgreSQL database. Business
modules remain folders inside layered projects. The approved direct references
are:

```text
Domain <- Application <- Infrastructure
                  ^             ^
                  |             |
             API and Worker hosts
Contracts <------- API
```

More precisely:

- Domain and Contracts reference no source project;
- Application references Domain;
- Infrastructure references Application and Domain;
- API references Application, Infrastructure, and Contracts;
- Worker references Application and Infrastructure;
- Web uses HTTP contracts and does not reference backend projects.

Architecture tests inspect project references and fail when this graph changes
without an explicit architecture decision.

## Consequences

The API and worker can scale independently while sharing business and
persistence code. Cross-module operations can use database transactions. A
module is split into a service only after production evidence shows independent
scale, release, ownership, or reliability needs.
