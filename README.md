# Lead Recovery Platform - Codex Documentation Package

## What this package is

This repository documentation defines a complete, implementation-ready plan for a **C# / ASP.NET Core lead-recovery platform** for Ontario home-service businesses. The first commercial use case is missed-call recovery for plumbing companies, followed by lead qualification, booking, quote follow-up, and staff handoff.

The recommended business strategy is service-first:

1. Build a reliable demonstration.
2. Sell a small fixed-scope pilot.
3. Install the same workflow for several paying clients.
4. Standardize recurring components.
5. Consider a niche micro-SaaS only after repeated paid validation.

The system intentionally uses **C# as the production backend** and includes **Docker and Kubernetes** as real deployment capabilities. Kubernetes is not allowed to block the first working MVP or first pilot.

## Current implementation status

Milestone 0 is complete, and LR-0101 plus LR-0201 through LR-0204 are
implemented. The repository
contains the modular-monolith solution, API and worker hosts, a persisted Tenant
aggregate, server-derived tenant context, EF Core migrations, the Lead
aggregate and lifecycle policy, tenant-isolated Customer, Lead, Conversation,
Message, and ScheduledAction persistence, the system-level external-event
receipt ledger, canonical phone normalization, deterministic message and action
state rules, project-boundary tests, PostgreSQL orchestration, and backend CI
quality gates. It deliberately does not yet contain feature API endpoints,
authentication, Twilio integration, Hangfire execution, or a Next.js
application.

The API exposes only the foundation health contract:

- `GET /health/live` reports whether the process is running;
- `GET /health/ready` reports whether registered readiness checks pass.

## Pinned foundation versions

| Component | Version | Milestone 0 use |
|---|---:|---|
| .NET SDK | 10.0.301 | Builds all backend projects |
| ASP.NET Core shared framework | 10.0.9 | API and worker runtime baseline |
| C# | 14.0 | Backend language version |
| PostgreSQL | 18.4 | Local database container |
| Entity Framework Core and tools | 10.0.9 | Persistence and migrations |
| Npgsql EF Core provider | 10.0.2 | PostgreSQL EF Core provider |
| libphonenumber-csharp | 9.0.34 | E.164 phone parsing and validation adapter |
| Testcontainers PostgreSQL | 4.13.0 | Isolated PostgreSQL integration tests |
| xUnit v3 Microsoft Testing Platform package | 3.2.2 | Backend test runner |
| Node.js | 24.17.0 | Reserved frontend runtime baseline |
| pnpm | 11.10.0 | Reserved frontend package manager baseline |
| Next.js | 16.2.10 | Reserved for Milestone 2 initialization |
| React | 19.2.7 | Reserved for Milestone 2 initialization |
| TypeScript | 6.0.3 | Reserved for Milestone 2 initialization |

Frontend versions are recorded now for reproducibility but no frontend package
is installed before Milestone 2.

## Local development

Prerequisites are Git, Docker Desktop with Compose, and the .NET SDK selected by
`global.json`. Node.js and pnpm are not required until Milestone 2.

Create a local environment file and replace the example database password:

```powershell
Copy-Item templates/.env.example .env
$env:ConnectionStrings__Database = 'Host=localhost;Port=5432;Database=leadrecovery;Username=leadrecovery;Password=<same-local-password>'
```

If Docker Desktop is running but the default Windows Docker context still
points at the inactive `docker_engine` pipe, select its Linux context for the
current shell with `$env:DOCKER_CONTEXT = 'desktop-linux'`.
Testcontainers also accepts that Docker context. Docker 24 supports API 1.43;
the integration fixture uses 1.43 when no `DOCKER_API_VERSION` override exists,
which remains backward-compatible with newer daemons.

Start PostgreSQL and verify the backend foundation:

```powershell
docker compose up -d postgres
dotnet tool restore
dotnet restore LeadRecovery.sln --locked-mode
dotnet ef database update --project src/LeadRecovery.Infrastructure --startup-project src/LeadRecovery.Api
dotnet format LeadRecovery.sln --verify-no-changes --no-restore
dotnet build LeadRecovery.sln --configuration Release --no-restore
dotnet test LeadRecovery.sln --configuration Release --no-build
dotnet run --project src/LeadRecovery.Api
```

With the API running, check `http://localhost:8080/health/live` and
`http://localhost:8080/health/ready`. Start the empty worker host separately
with `dotnet run --project src/LeadRecovery.Worker` when process wiring needs to
be checked.

Stop local services without deleting data:

```powershell
docker compose down
```

To intentionally reset the local database, use `docker compose down --volumes`.
That command permanently removes the Compose-managed local PostgreSQL volume.
For a disposable development database, the EF migration can instead be rolled
back with:

```powershell
dotnet ef database update 0 --project src/LeadRecovery.Infrastructure --startup-project src/LeadRecovery.Api
```

Do not use either reset procedure on a database containing data that must be
retained.

## Start here

For Codex or another coding agent:

1. Place this package at the root of the implementation repository.
2. Ensure `AGENTS.md` remains at the repository root.
3. Give the agent `CODEX_START_HERE.md` as its first task context.
4. Ask it to execute only one milestone or issue at a time.
5. Require tests, documentation updates, and a summary after every task.

For a single-file upload, use:

- `CODEX_MASTER_IMPLEMENTATION_SPEC.md`

For repository-based work, use the modular files under `docs/`.

## Core product

**Working name:** LeadRecovery

**Initial customer:** Ontario plumbing companies with approximately 3-20 employees.

**Initial outcome:** When a business misses a call, the caller receives a prompt SMS, can provide job details, receives a booking or callback path, and appears as a structured lead in the staff dashboard.

**Commercial promise:** Fewer missed opportunities, faster response time, less office administration.

## Documentation map

| File | Purpose |
|---|---|
| `AGENTS.md` | Permanent instructions for Codex and coding agents |
| `CODEX_START_HERE.md` | First prompt and execution protocol |
| `CODEX_MASTER_IMPLEMENTATION_SPEC.md` | Complete combined specification |
| `CODEX_PROMPT_SEQUENCE.md` | Ready-to-use prompts for each build phase |
| `docs/01_PRODUCT_REQUIREMENTS.md` | Scope, users, workflows, requirements |
| `docs/02_SYSTEM_ARCHITECTURE.md` | Architecture, components, patterns, decisions |
| `docs/03_DOMAIN_AND_DATABASE.md` | Entities, states, constraints, schema |
| `docs/04_API_AND_INTEGRATIONS.md` | API, Twilio, booking, AI contracts |
| `docs/05_FRONTEND_UX.md` | Dashboard screens, behavior, accessibility |
| `docs/06_AI_GUARDRAILS.md` | Safe AI use, structured outputs, fallbacks |
| `docs/07_SECURITY_PRIVACY.md` | Security controls, privacy, tenant isolation |
| `docs/08_TESTING_QUALITY.md` | Test pyramid, scenarios, release gates |
| `docs/09_DEVOPS_KUBERNETES.md` | Docker, Kubernetes, CI/CD, environments |
| `docs/10_OBSERVABILITY_OPERATIONS.md` | Logs, metrics, alerts, support runbooks |
| `docs/11_TIMELINE_AND_MILESTONES.md` | Detailed implementation timeline |
| `docs/12_BACKLOG_AND_ACCEPTANCE.md` | Epics, stories, acceptance criteria |
| `docs/13_PILOT_AND_VALIDATION.md` | Demo, pilot onboarding, market validation |
| `docs/14_SAAS_EVOLUTION.md` | Productization and later SaaS roadmap |
| `docs/decisions/` | Accepted architecture decision records |
| `api/openapi.yaml` | Initial API contract skeleton |
| `database/schema.sql` | Reference relational schema |
| `CHANGELOG.md` | User-visible and repository-level changes |
| `templates/.env.example` | Required environment variables |
| `templates/definition-of-done.md` | Completion checklist |
| `templates/pull-request-template.md` | Pull request quality template |

## Baseline delivery timeline

The baseline assumes **20-25 focused hours per week for 10 weeks**. A full-time developer can compress it, but the order of work should remain the same.

- Weeks 1-2: foundation, domain, persistence, authentication
- Weeks 3-4: Twilio calls/SMS, leads, background jobs
- Weeks 5-6: dashboard, booking, follow-ups, AI summaries
- Weeks 7-8: security, testing, observability, deployment
- Week 9: Kubernetes and CI/CD demonstration
- Week 10: pilot readiness, documentation, sales demo

## Non-negotiable product principles

- Business outcomes before technical novelty.
- Modular monolith before microservices.
- Deterministic workflows before autonomous agents.
- Human override for every automation.
- No secrets in source control.
- Every external webhook is authenticated and idempotent.
- Every tenant query is tenant-scoped.
- No production release without automated tests and a rollback path.
- Kubernetes is a deployment option, not the product.
