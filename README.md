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

Milestones 0 through 6 are complete. LR-0101 through LR-0604, LR-0701, and
LR-0702 are implemented.
The modular monolith now includes the PostgreSQL domain and tenant foundation,
secure Identity cookie sessions, signed Twilio call/SMS ingestion,
PostgreSQL-backed Hangfire recovery and manual-message execution, immediate
opt-out suppression, deterministic tenant-configured qualification and
follow-up workflows, approved booking links, business-hours scheduling, and
the operational Next.js dashboard. Staff can filter
the tenant inbox, inspect the ordered call/SMS/system/note timeline, assign and
transition Leads, send idempotent manual SMS, and pause or resume eligible
automation. All browser writes use CSRF and role authorization; Lead writes use
opaque optimistic-concurrency tokens and return the latest safe representation
on conflicts. Unit, PostgreSQL integration, performance, and Playwright tests
cover these flows without enabling live SMS.

The implemented dashboard now uses one responsive, high-contrast workspace
system across login, inbox, and Lead detail. Human-readable workflow labels,
attention-first queue rows, clearer loading/empty/error feedback, consistent
44-pixel controls, skip navigation, reduced-motion support, and mobile overflow
coverage improve daily use without adding a component-library dependency or
changing an API/workflow contract.

LR-0701 adds a provider-neutral analysis contract, independent strict schema
validation, and an optional OpenAI Responses API adapter. The adapter is
disabled by default, sends a redacted and bounded recent transcript with
`store: false`, and returns typed failures for timeouts, refusals, HTTP errors,
or invalid output. LR-0702 now persists immutable validated suggestions and
adds tenant-scoped accept, correct, and reject controls with low-confidence
labels, optimistic review concurrency, and redacted audit history. Suggested
replies remain visibly unsent drafts and no review action creates a Message or
ScheduledAction. Automatic invocation and outage fallback remain LR-0703.

The currently implemented browser and health contract is:

- `GET /health/live` reports whether the process is running;
- `GET /health/ready` reports whether registered readiness checks pass;
- `GET /api/v1/auth/csrf`, `POST /api/v1/auth/login`,
  `GET /api/v1/auth/me`, and `POST /api/v1/auth/logout` manage the browser
  session;
- `GET /api/v1/leads`, `GET /api/v1/leads/assignees`, and
  `GET /api/v1/leads/{leadId}` provide the filtered inbox, eligible tenant
  assignees, ordered timeline, structured qualification answers, approved
  booking destination, pending actions, and allowed transitions;
- Lead detail also projects immutable AI suggestions and separate staff review
  values; accept/edit/reject routes use CSRF, operator roles, tenant scope, and
  an opaque analysis version without sending customer content;
- lead assignment, transition, note, manual-message, pause, and resume endpoints
  are CSRF-protected and restricted to Owner, Manager, and Staff memberships;
- booking-link queue and pending-action cancellation endpoints use the same
  tenant, role, CSRF, and concurrency controls;
- `POST /api/v1/webhooks/twilio/call-status` accepts only correctly signed
  form callbacks and records recovery intent;
- `POST /api/v1/webhooks/twilio/sms/inbound` and
  `POST /api/v1/webhooks/twilio/sms/status` validate signed callbacks, persist
  inbound activity once, apply opt-out suppression, and update delivery state;
- the worker executes due recovery, qualification, booking, follow-up, and
  manual-message actions through
  PostgreSQL-backed Hangfire,
  using the deterministic fake SMS provider unless real delivery is explicitly
  enabled.

## Pinned foundation versions

| Component | Version | Current use |
|---|---:|---|
| .NET SDK | 10.0.301 | Builds all backend projects |
| ASP.NET Core shared framework | 10.0.9 | API and worker runtime baseline |
| C# | 14.0 | Backend language version |
| PostgreSQL | 18.4 | Local database container |
| Entity Framework Core and tools | 10.0.9 | Persistence and migrations |
| Microsoft.Extensions.Http | 10.0.9 | Typed HTTP client for optional analysis |
| Npgsql EF Core provider | 10.0.2 | PostgreSQL EF Core provider |
| libphonenumber-csharp | 9.0.34 | E.164 phone parsing and validation adapter |
| Twilio .NET SDK | 7.14.9 | Webhook signature validation and gated outbound adapter |
| Hangfire ASP.NET Core | 1.8.23 | Worker server and retry policy |
| Hangfire PostgreSQL | 1.21.1 | Durable background-job storage |
| Testcontainers PostgreSQL | 4.13.0 | Isolated PostgreSQL integration tests |
| xUnit v3 Microsoft Testing Platform package | 3.2.2 | Backend test runner |
| Node.js | 24.17.0 | Frontend and Playwright runtime |
| pnpm | 11.10.0 | Locked frontend workspace package manager |
| Next.js | 16.2.10 | Same-origin browser shell |
| React | 19.2.7 | Browser UI runtime |
| TypeScript | 6.0.3 | Strict frontend type checking |
| Playwright | 1.61.1 | Browser acceptance tests |
| Default OpenAI analysis model | gpt-5.6-sol | Operator-overridable structured analysis default |

## Local development

Prerequisites are Git, Docker Desktop with Compose, the .NET SDK selected by
`global.json`, Node.js from `.node-version`, and pnpm from `package.json`.

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

For the authenticated demo, fill the `DemoSeed__*` values documented in
`templates/.env.example`, enable the demo seed only in a disposable local
database, and start the frontend in a second shell:

```powershell
corepack enable
pnpm install --frozen-lockfile
$env:API_BASE_URL = 'http://localhost:8080'
pnpm frontend:dev
```

The browser uses the Next.js `/api` rewrite so the session and antiforgery
cookies remain same-origin. Do not expose the API under a separate browser
origin or let the client supply `TenantId`.

To exercise the Twilio webhook endpoints, set `TWILIO_AUTH_TOKEN` and the
exact public application base in `TWILIO_WEBHOOK_BASE_URL`. The latter is used
to reconstruct the signed public URL behind a trusted proxy. Leave both unset
when the webhooks are not enabled; the endpoints then fail closed with `503`.

The worker is safe by default: `SMS_PROVIDER=fake` produces a deterministic
provider SID without network access. A live Twilio request is possible only
when `SMS_PROVIDER=twilio` and `ALLOW_REAL_SMS=true` are both set and the
Twilio account SID/auth token are present. Keep the fake defaults for automated
tests and local workflow development.

AI analysis also stays disabled by default. LR-0701 registers the adapter only
when `AI_ENABLED=true`; provide `OPENAI_API_KEY`, an explicit `AI_MODEL`
(default `gpt-5.6-sol`), and the bounded timeout/retry settings from
`templates/.env.example`. The current Worker has no analysis job yet, so
enabling this registration alone does not persist a suggestion or send any
customer-facing content.

The fictional demo seed includes one pending low-confidence analysis so the
LR-0702 review workflow can be demonstrated without enabling AI or providing an
API key.

With the API running, check `http://localhost:8080/health/live` and
`http://localhost:8080/health/ready`. Start the worker separately after setting
the same database connection and webhook base URL:

```powershell
$env:SMS_PROVIDER = 'fake'
$env:ALLOW_REAL_SMS = 'false'
dotnet run --project src/LeadRecovery.Worker
```

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
