# AGENTS.md - Repository Instructions for Codex

## Mission

Build and maintain the LeadRecovery platform according to the specifications in this repository. The product helps home-service businesses recover missed callers, qualify leads, schedule follow-up, and give staff a reliable dashboard.

## Required reading order

Before changing code, read:

1. `README.md`
2. `docs/01_PRODUCT_REQUIREMENTS.md`
3. `docs/02_SYSTEM_ARCHITECTURE.md`
4. The document relevant to the assigned task
5. `templates/definition-of-done.md`

When requirements conflict, stop and report the conflict. Do not silently choose a new product direction.

## Architecture rules

- Start as a **modular monolith**, not microservices.
- Backend: C# and ASP.NET Core on the latest supported .NET LTS chosen at repository initialization.
- Persistence: PostgreSQL through Entity Framework Core migrations.
- Frontend: React/Next.js with TypeScript.
- Background work: Hangfire with PostgreSQL-backed storage unless an approved architecture decision changes it.
- Integrations: Twilio, booking provider, email provider, and optional OpenAI analysis.
- Local orchestration: Docker Compose.
- Portfolio/cloud deployment: Kubernetes manifests or Helm after the Docker deployment works.
- Shared database multi-tenancy with mandatory `TenantId` scoping.

## Coding rules

- Use nullable reference types.
- Treat compiler warnings as errors in CI.
- Use asynchronous I/O and pass `CancellationToken` through application boundaries.
- Keep controllers thin. Business logic belongs in application services or domain policies.
- Validate all external input.
- Use idempotency for webhook processing.
- Never log secrets, access tokens, full authentication cookies, or unnecessary message content.
- No hard-coded credentials, phone numbers, tenant IDs, URLs, or pricing.
- Prefer explicit types and readable code over clever abstractions.
- Do not introduce a dependency without explaining why it is needed.
- Do not add a microservice, message broker, service mesh, Redis, or event-sourcing framework unless a documented requirement justifies it.

## Testing rules

For every behavior change:

1. Add or update unit tests.
2. Add integration tests when persistence, authentication, webhooks, or external adapters are involved.
3. Run formatting, build, and all relevant tests.
4. Report the exact commands and outcomes.

Critical flows require end-to-end coverage:

- missed call -> lead creation -> automatic SMS
- inbound SMS -> message persisted -> lead updated
- staff closes or books lead -> scheduled follow-ups stop
- opt-out -> no further automated messages
- duplicate webhook -> no duplicate lead or message
- cross-tenant access -> denied

## Security rules

- Validate Twilio signatures.
- Require HTTPS outside local development.
- Store secrets in environment variables or a secret manager.
- Use secure, HttpOnly cookies for browser sessions when practical.
- Protect state-changing browser requests against CSRF.
- Enforce tenant authorization in both application logic and data access.
- Redact sensitive values in logs.
- Apply least privilege to Kubernetes service accounts and cloud identities.

## AI rules

AI is an optional assistant, not the workflow controller.

- Use structured JSON output with a versioned schema.
- AI may categorize, summarize, and draft.
- AI may not promise prices, diagnose trade problems, guarantee arrival times, or close a lead without business rules or staff action.
- Low-confidence or safety-sensitive results must be routed to human review.
- The deterministic workflow must continue if the AI provider is unavailable.

## Task execution protocol

For each assigned task:

1. Restate the task and identify affected components.
2. List assumptions and open questions.
3. Propose a small implementation plan.
4. Make the smallest coherent change.
5. Add tests.
6. Run validation commands.
7. Update relevant documentation and changelog.
8. Return a concise summary, files changed, tests run, risks, and next recommended issue.

Do not implement future milestones unless explicitly asked.

## Definition of done

A task is complete only when it meets `templates/definition-of-done.md` and its acceptance criteria in `docs/12_BACKLOG_AND_ACCEPTANCE.md`.
