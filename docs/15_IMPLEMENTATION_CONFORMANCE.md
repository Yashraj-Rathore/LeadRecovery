# 15 - Implementation Conformance and Readiness

## 1. Purpose and scope

This document reconciles the product requirements, architecture, accepted
decisions, API contract, database reference, deployment assets, and executable
implementation as audited on 2026-07-29. The current product is a modular
monolith with separate API, Worker, and Next.js hosts over one PostgreSQL
database. It is a service-led pilot product, not a self-service SaaS.

`api/openapi.yaml` is the exact implemented HTTP surface. Product documents may
also describe future contracts; those are not current routes unless they appear
in OpenAPI and endpoint mapping tests.

## 2. Current end-to-end interactions

| Interaction | Current behavior and evidence boundary |
|---|---|
| Authentication and tenancy | Same-origin secure cookie and CSRF flow; one active tenant membership per session; role checks and server-derived TenantId; list/detail/mutation and persistence tests deny cross-tenant access. |
| Missed call recovery | Signed call-status callback resolves the configured destination, records one opaque receipt, creates/updates one Lead, and schedules one recovery intent under cooldown and automation policy. Duplicate delivery has no duplicate business effect. |
| Outbound recovery | Worker rechecks global, tenant, Lead, opt-out, number, and approved-template policy before a fake-by-default or explicitly gated Twilio call. Rendered templates allow only bounded BusinessName/BookingUrl substitutions. |
| Inbound SMS and STOP | Signed inbound callback persists one message and activity update. STOP-family input stores opt-out state, suppresses the Lead, and cancels pending automation transactionally. |
| Delivery lifecycle | Signed callbacks apply allowed Sent/Delivered/Failed progression idempotently and expose failure in the timeline. |
| Staff workspace | Accessible responsive inbox/detail/report routes provide filters, assignment, audited state transitions, notes, manual SMS, lead/tenant automation controls, pending-action cancellation, booking flow, conflict refresh, and role-aware human review. |
| Qualification and follow-up | Versioned tenant policy deterministically evaluates structured answers, schedules only inside tenant business hours, caps follow-ups at three, and rechecks reply/closure/booking/opt-out state at execution. |
| AI assistance | Optional and disabled by default. Bounded redacted input, strict versioned output, immutable suggestion, accept/edit/reject review, and outage fallback never control deterministic workflow or send customer content. |
| Operational controls | Platform and tenant kill switches fail closed without disabling inbound capture, dashboard access, delivery callbacks, or manual staff SMS. Retention is opt-in, dry-run first, tenant-scoped, audited, and deletion requires backup acknowledgement. |
| Pilot reporting | Authenticated tenant-scoped JSON, CSV, and UI share one bounded definition for operational—not revenue or causal—metrics. |
| Deployment | Multi-stage non-root images, migration-first Compose/Kustomize, external secrets/database, health probes, immutable-digest CI/CD, staging-before-production promotion, and non-migrating rollback are policy-tested. |

## 3. Corrections made by this audit

- Compose now passes `AUTOMATION_GLOBAL_ENABLED` and `AI_ENABLED` to both API
  and Worker, plus the documented host-specific settings. Previously the Worker
  could be enabled while the API silently remained disabled and scheduled no
  automated or AI work.
- Worker delivery callbacks now preserve a configured public path prefix and
  reject credential/query/fragment URLs plus non-HTTPS production bases.
- Template activation validates the fully substituted SMS length and supported
  placeholder set. Runtime processing also fails an invalid legacy template
  safely before creating or sending a Message.
- The Next.js document host now emits its own CSP, frame, MIME, referrer,
  permissions, and cross-domain security headers; API headers alone did not
  protect separately served HTML.
- The Worker's recent-dispatch suppression cache now expires entries after the
  execution lease instead of growing for the lifetime of the process.
- The committed OpenAPI contract now has stable operation IDs, warning-free
  linting, and an exact runtime route/method conformance test under ADR-0026.
- The standalone Next.js build now packages static assets into its runnable
  output, so local, browser-test, and container startup use the same artifact.
- Tenant onboarding can now validate and configure the existing opt-in
  retention policy instead of requiring a direct database edit.
- Current and future settings, reporting, voice, notification, and data models
  are now labelled explicitly throughout the source documentation.

## 4. Intentionally deferred product surfaces

These are not defects in the current LR-0001 through LR-1003 pilot scope:

- self-service business/template/integration settings and user invitations;
- stored service-area and notification-recipient settings; pilots must document
  those rules operationally until an approved settings/notification issue;
- forgot/reset password, tenant switching, and PlatformAdmin browser screens;
- a TwiML voice route, separate CallEvent history, or voice recording;
- direct calendar booking integration or booking webhooks; the current flow
  uses one approved HTTPS booking destination and staff confirmation;
- external email notifications and a Notification table; urgent work is
  represented in the operational inbox and audit trail;
- broad overview/funnel/failure analytics beyond the bounded pilot report;
- SignalR/live push, billing, usage metering, CRM integrations, and SaaS
  cancellation/export/deletion workflows.

These surfaces require a backlog issue and acceptance criteria before
implementation. Tenant operational retention is implemented; it is not a
substitute for a future legal tenant export/deletion workflow.

## 5. Evidence that cannot be claimed from a local automated audit

The repository proves behavior with fake/in-process providers and disposable
PostgreSQL. Before a real pilot, an operator must still record:

- real Twilio number routing, exact public callback signatures, carrier
  delivery, consent wording, allowed recipients, and rollback rehearsal;
- hosted TLS/ingress, managed database backup and restore, alert destinations,
  secrets, cluster credentials, and protected-environment reviewers;
- the sustained 20-webhook/second, 100-concurrent-read, and 1,000-action staging
  runs with p50/p95/p99, error, connection, lag, CPU, memory, and duplicate data;
- the human-labelled 100-message AI evaluation and agreed accuracy, safety,
  latency, and cost thresholds before enabling AI for production;
- accessibility review with assistive technology and the pilot business's
  approved templates, hours, support owner, measurement baseline, and privacy
  agreements.

Absence of those environment/provider results must not be reported as a
software test failure, but the product must remain disabled or fake-by-default
until the applicable gate is approved.

## 6. Audit validation commands

The repository definition of done uses the following evidence set:

```powershell
dotnet format LeadRecovery.sln --no-restore --verify-no-changes
dotnet build LeadRecovery.sln --configuration Release --no-restore --warnaserror
dotnet test LeadRecovery.sln --configuration Release --no-build
pnpm frontend:typecheck
$env:API_BASE_URL = 'http://127.0.0.1:8080'
pnpm frontend:build
pnpm openapi:lint
pnpm audit --audit-level high
./eng/Test-DeploymentArtifacts.ps1
./eng/Test-CiCdArtifacts.ps1
./eng/Invoke-DemoProof.ps1 -Configuration Release
pnpm e2e
```

Integration and browser commands require Docker and a fresh disposable
database. Live SMS and AI remain disabled for every automated command.
