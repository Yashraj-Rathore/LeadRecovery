# Codex Prompt Sequence

Use one prompt at a time. Do not ask Codex to build the entire system in one task.

## Prompt 0 - Repository assessment

Read `AGENTS.md`, `README.md`, and all files under `docs/`. Do not write code yet. Return a repository plan, pinned technology choices, identified ambiguities, and a proposed issue order mapped to `docs/12_BACKLOG_AND_ACCEPTANCE.md`.

## Prompt 1 - Foundation

Implement LR-0001, LR-0002, and LR-0003 only. Create the solution structure, architecture tests, code-quality configuration, CI skeleton, and local PostgreSQL setup. Reserve `src/LeadRecovery.Web` with documentation only; do not initialize Next.js yet. Add documentation for local commands. Run all validation and stop.

## Prompt 2 - Domain and persistence

Implement LR-0101, LR-0102, LR-0201, LR-0202, LR-0203, and LR-0204. Use EF Core with PostgreSQL, add migrations, Testcontainers integration tests, tenant isolation tests, and lead state-transition tests. Implement tenant context only; authentication and tenant membership remain Milestone 2. Do not implement Twilio, Hangfire execution, or UI.

## Prompt 3 - Authentication and frontend shell

Implement LR-0103 plus the minimum authenticated frontend shell needed to display seeded tenant-scoped leads. Use secure same-origin session design. Include E2E login and cross-tenant denial tests.

## Prompt 4 - Twilio call ingestion

Implement LR-0301, LR-0302, and LR-0303. Use a provider adapter and fixtures. Validate signatures, resolve tenant from destination number, write idempotency receipt, create/update lead, and schedule recovery action. Do not send live SMS yet.

## Prompt 5 - Worker and SMS

Implement LR-0401 through LR-0405. Use Hangfire with PostgreSQL storage, a fake adapter for automated tests, and a real adapter behind configuration. Include opt-out and duplicate-job tests. Provide a safe local test procedure.

## Prompt 6 - Dashboard operations

Implement LR-0501 through LR-0505. Build lead inbox/detail, assignment, allowed transitions, manual messaging, pause/resume, pending actions, loading/error/concurrency states, and accessibility checks.

## Prompt 7 - Qualification and booking

Implement LR-0601 through LR-0604. Keep rules deterministic. Add business-hours and DST tests. Booking may be a tenant-configured link; do not add unnecessary calendar integrations.

## Prompt 8 - AI assistance

Implement LR-0701 through LR-0703. Use strict structured output, minimum data, human review, confidence handling, and fallback. No autonomous customer-facing generation.

## Prompt 9 - Hardening

Implement LR-0801 through LR-0804. Add telemetry, kill switch, retention dry-run, rate limiting, security headers, alerts/runbooks, and PII-safe logs.

## Prompt 10 - Containers and Kubernetes

Implement LR-0901 and LR-0902. First prove Docker Compose works, then add Kubernetes base/overlays, probes, resources, migration job, ingress, and secret references. Demonstrate a rolling update and pod restart recovery.

## Prompt 11 - CI/CD

Implement LR-0903. Add PR gates, image build/scan, staging deployment, smoke test, approval gate, production deployment, and rollback documentation. Never place credentials in workflow files.

## Prompt 12 - Pilot readiness

Implement LR-1001 through LR-1003. Create fictional demo seed data, onboarding checklist, demo instructions, operational metrics, case-study README, and a two-minute demo flow.

## Prompt format for defect fixes

Investigate defect `[description]`. Reproduce it with an automated failing test before fixing it. Identify root cause, make the smallest safe correction, run relevant and full regression tests, update documentation if behavior changes, and report any remaining risk.

## Prompt format for architecture review

Review the implementation against `docs/02_SYSTEM_ARCHITECTURE.md`, `docs/07_SECURITY_PRIVACY.md`, and `AGENTS.md`. Do not refactor automatically. Return violations ranked by severity, evidence with file paths, and a staged correction plan.
