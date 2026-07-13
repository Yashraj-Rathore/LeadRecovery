# ADR-0002: Pinned technology baseline

- Status: Accepted
- Date: 2026-07-13

## Context

The specifications selected technology families but did not pin a reproducible
initial toolchain. Floating versions would make local and CI behavior diverge.

## Decision

Use this foundation baseline:

| Component | Pinned version |
|---|---:|
| .NET SDK | 10.0.301 |
| Target framework | net10.0 |
| ASP.NET Core and Microsoft extension packages | 10.0.9 |
| C# | 14.0 |
| PostgreSQL container | postgres:18.4-bookworm |
| Entity Framework Core and dotnet-ef | 10.0.9 |
| Npgsql Entity Framework Core provider | 10.0.2 |
| Testcontainers.PostgreSql | 4.13.0 |
| xUnit v3 Microsoft Testing Platform package | 3.2.2 |
| Node.js | 24.17.0 |
| pnpm | 11.10.0 |
| Next.js | 16.2.10 |
| React and React DOM | 19.2.7 |
| TypeScript | 6.0.3 |

`global.json`, central package management, lock files, `.node-version`, and the
future frontend lock file enforce the applicable versions. The Node.js and
frontend versions are reserved in Milestone 0; Next.js packages are not
installed until Milestone 2.

LR-0101 introduces and centrally pins EF Core, its design-time tooling, the
Npgsql provider, and PostgreSQL Testcontainers. Hangfire and its PostgreSQL
provider are selected and pinned only when job execution is introduced in
Milestone 3. Deferring unused dependencies avoids speculative packages.

## Consequences

Local development and CI use the same SDK and package graph. Changing a major
runtime, database, or framework version requires an ADR and full validation.
Patch updates may use a normal dependency change with passing quality gates.
