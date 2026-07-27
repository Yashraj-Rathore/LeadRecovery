# 12 - Backlog, User Stories, and Acceptance Criteria

## How to use this backlog

Codex should implement one issue at a time. Each issue must meet its acceptance criteria and the repository definition of done.

## Epic E0 - Foundation

### LR-0001 Create solution structure

**Story:** As a developer, I need a clean solution structure so modules remain testable and dependencies are controlled.

**Acceptance:**

- solution contains Domain, Application, Infrastructure, Contracts, Api, Worker,
  and test projects;
- `src/LeadRecovery.Web` is reserved with documentation only; Next.js is not
  initialized before Milestone 2;
- project references follow architecture rules;
- architecture test prevents Domain from referencing Infrastructure/API;
- clean build succeeds.

### LR-0002 Configure code quality

**Acceptance:**

- nullable enabled;
- warnings as errors in CI;
- editorconfig committed;
- formatter command documented;
- analyzers enabled;
- CI runs format/build/test.

### LR-0003 Local PostgreSQL

**Acceptance:**

- Docker Compose starts PostgreSQL;
- health check exists;
- secrets come from local environment;
- README includes start/stop/reset commands.

## Epic E1 - Tenancy and identity

### LR-0101 Tenant entity and context

**Acceptance:**

- Tenant entity persisted;
- active tenant context server-derived;
- missing tenant fails closed;
- tenant configuration uses an application-managed concurrency version;
- unit/integration tests included.

### LR-0102 Tenant-scoped data access

**Acceptance:**

- tenant-owned entities include TenantId;
- query filter or equivalent applied;
- cross-tenant read/write tests fail safely;
- browser cannot override TenantId.

### LR-0103 Authentication and roles

**Acceptance:**

- Owner and Staff users can authenticate;
- secure cookie/session configuration;
- logout invalidates session;
- unauthorized access returns proper status;
- role policies tested.

Implemented in Prompt 3 with ASP.NET Core Identity, explicit
`TenantMembership`, Owner/Manager/Staff/ReadOnly tenant roles, per-request user
and membership validation, same-origin HttpOnly cookie sessions, CSRF,
lockout/rate limiting, security-stamp logout invalidation, and audit events.
Owner and Staff authentication plus Owner-only policy behavior are tested.
PlatformAdmin and tenant switching are intentionally deferred; login fails
closed if there is not exactly one Trial/Active membership.

## Epic E2 - Lead domain

### LR-0201 Lead aggregate

**Acceptance:**

- required fields and enums implemented;
- allowed transitions enforced;
- closure requires reason;
- Booked and ClosedWon are statuses, not close reasons;
- booking cancels automation through application use case;
- domain tests cover all transitions.

For LR-0201, every pre-booking active status may route to `NeedsHuman` or
`Closed`; closure requires a documented reason. `Closed` and `ClosedWon` remain
terminal until a later audited reopening use case is implemented. LR-0204 now
connects durable scheduled-action cancellation behind the booking use case.

### LR-0202 Customer and phone normalization

**Acceptance:**

- E.164 normalization adapter;
- tenant-scoped customer uniqueness;
- invalid/unknown numbers handled explicitly;
- no duplicate customer from equivalent formatting.

LR-0202 stores canonical E.164 phone identity behind an application interface,
derives customer ownership from server tenant context, and enforces
`(TenantId, PhoneE164)` uniqueness in PostgreSQL. Its Customer-specific query
and write guards do not complete LR-0102, which remains open for the other
tenant-owned Milestone 1 entities.

### LR-0203 Conversation and message model

**Acceptance:**

- inbound/outbound messages persisted;
- provider SID and client idempotency key constraints;
- delivery-state transitions validated;
- message body length policy enforced.

LR-0203 persists Lead as the required parent plus tenant-owned Conversation and
Message records. Inbound messages start `Received`; outbound messages follow
`Queued -> Sent -> Delivered`, may fail while queued or sent, and may be
suppressed while queued. Final states are terminal. The database enforces global
provider identity within a provider and tenant-scoped client idempotency, while
the domain enforces a 1,600-character body ceiling. No Twilio calls, webhook
handlers, Hangfire execution, authentication, or feature API endpoints are part
of this issue.

### LR-0204 Scheduled action and external receipt persistence

**Acceptance:**

- scheduled actions are tenant-scoped and uniquely idempotent within a tenant;
- due-action indexes and validated action status transitions are included;
- external event receipts enforce a unique opaque provider event key;
- a receipt may have a nullable immutable TenantId because tenant resolution can
  fail or occur after durable receipt;
- legitimate provider status progressions are not collapsed as duplicates;
- mappings, migrations, and PostgreSQL integration tests are included;
- no Twilio calls, Hangfire execution, or other external side effects are added.

LR-0204 stores tenant-owned ScheduledAction intent with explicit state
transitions, tenant-key uniqueness, due and cancellation indexes, and compound
Lead ownership. Booking now persists the Lead transition and cancels only its
Pending actions through one scoped EF transaction. ExternalEventReceipt is a
system ledger with a unique opaque `(Provider, EventType, ExternalEventId)` key;
TenantId is nullable until resolved and immutable afterward. Unit tests cover
the transition and receipt policies, and PostgreSQL tests cover migration,
tenant denial, uniqueness, status progression, and durable cancellation. This
issue does not dispatch scheduled work or call an external provider.

## Epic E3 - Twilio calls

### LR-0301 Twilio signature validator

**Acceptance:**

- valid fixture accepted;
- invalid fixture rejected 403;
- canonical URL works behind configured proxy;
- secret never logged.

### LR-0302 Provider number mapping

**Acceptance:**

- destination number resolves exactly one active tenant;
- unknown number rejected/acknowledged according to policy without creating data;
- suspended tenant cannot trigger sends.

### LR-0303 Process missed call

**Acceptance:**

- configured status creates/updates lead;
- scheduled recovery action created;
- duplicate event has no duplicate effect;
- cooldown prevents repeated texts;
- audit and metrics emitted.

Implementation status (2026-07-15): LR-0301, LR-0302, and LR-0303 are
complete. Signature validation uses the pinned official Twilio SDK and a
configured canonical public base URL. Provider destinations are globally unique
and tenant-owned; Trial/Active tenants require both global and number-level
automation enablement. Valid events are handled in one serializable transaction
with opaque receipt identity, lead create/update, pending recovery action,
redacted audit, and fixed-cardinality metrics. Duplicate, cooldown, unknown,
non-recoverable, and suspended outcomes are safely acknowledged without a
duplicate or prohibited business action. No outbound provider call or Hangfire
execution is included.

## Epic E4 - SMS and jobs

### LR-0401 Background job infrastructure

**Acceptance:**

- Hangfire uses PostgreSQL storage;
- worker processes job;
- retry policy distinguishes transient/permanent failure;
- job correlation fields logged;
- duplicate execution is safe.

### LR-0402 Send recovery SMS

**Acceptance:**

- uses approved active template;
- re-checks eligibility at execution time;
- opt-out/paused/booked lead suppresses send;
- message record persisted;
- provider response mapped;
- test adapter verifies payload.

### LR-0403 Process inbound SMS

**Acceptance:**

- signature validated;
- inbound message persisted once;
- associated lead updated;
- dashboard activity event emitted;
- unknown number policy tested.

### LR-0404 Opt-out

**Acceptance:**

- recognized opt-out updates customer/lead suppression;
- pending SMS actions cancelled;
- future automated send blocked;
- audit event created;
- E2E test passes.

### LR-0405 Delivery status callbacks

**Acceptance:**

- message status updated idempotently;
- permanent failure visible;
- no blind retry on invalid/unsubscribed number;
- metrics emitted.

Implementation note (2026-07-15): LR-0401 through LR-0405 are complete.
Hangfire 1.8.23 uses PostgreSQL storage through Hangfire.PostgreSql 1.21.1 in
the Worker process. Sends use an approved active tenant template, persist before
the provider call, re-check eligibility, and are duplicate-safe. Signed inbound
SMS and delivery callbacks use opaque receipts; STOP-family input suppresses
the customer/lead and cancels pending recovery work atomically. Provider
failures are classified before retry, outcomes are audited and metered, and a
real Hangfire PostgreSQL integration test proves worker execution.

## Epic E5 - Dashboard

### LR-0501 Lead inbox

**Acceptance:**

- authenticated tenant-scoped list;
- filters for status, urgency, assignment;
- empty/loading/error states;
- keyboard accessible;
- performance target with 10,000 seeded leads.

Prompt 3 provided only the minimum read-only authenticated shell needed to
prove LR-0103. Prompt 6 has now added status/urgency/assignment filters,
loading behavior, lead navigation, and the 10,000-Lead performance acceptance
required to complete LR-0501.

### LR-0502 Lead detail and timeline

**Acceptance:**

- call, SMS, system, and note events ordered consistently;
- no raw HTML execution;
- pending actions visible;
- latest data refresh behavior documented.

### LR-0503 Assignment and transitions

**Acceptance:**

- assign self/other authorized user;
- transition endpoint uses domain rules;
- optimistic concurrency conflict shown;
- audit event stored.

### LR-0504 Manual message

**Acceptance:**

- authorized staff only;
- opt-out and policy checked;
- idempotency key used;
- send/failure shown in timeline;
- manual messages labeled.

### LR-0505 Pause/resume automation

**Acceptance:**

- pause cancels/suppresses pending actions;
- resume creates only valid future actions;
- action is audited;
- UI state is obvious.

Implementation note (2026-07-15): LR-0501 through LR-0505 are complete. The
tenant inbox filters before paging and meets the 10,000-Lead p95 target in a
real PostgreSQL acceptance test. Detail projects call audit, SMS, system, and
tenant-owned note records into a stable plain-text timeline and exposes pending
work. Owner/Manager/Staff writes require CSRF, active membership, entity tenant
scope, domain transitions, and opaque Lead versions; stale writes return the
latest representation. Manual SMS is idempotently persisted before a
fake-by-default Worker send and re-checks opt-out at execution. Pause cancels
pending automated intent, while resume schedules only an eligible missed-call
recovery that was never sent. Playwright verifies filters, keyboard focus,
conflict recovery, notes, manual messaging, automation state, and tenant denial.

## Epic E6 - Qualification and booking

### LR-0601 Deterministic qualification

**Acceptance:**

- tenant-configured questions;
- answer collection updates structured fields;
- unknown/ambiguous response routes to human;
- no AI dependency.

### LR-0602 Business-hours scheduler

**Acceptance:**

- tenant timezone honored;
- DST tests pass;
- after-hours action moved to next permitted window;
- urgent human notification can follow separate policy.

### LR-0603 Booking link

**Acceptance:**

- approved URL only;
- link sent once per configured stage;
- staff can mark booked;
- booking cancels follow-ups.

### LR-0604 Follow-up cadence

**Acceptance:**

- actions scheduled from tenant policy;
- eligibility rechecked at execution;
- maximum number enforced;
- all actions visible/cancellable;
- no sends after closure/opt-out.

Implementation note (2026-07-21): LR-0601 through LR-0604 are complete. A
versioned tenant workflow validates ordered required-text/choice questions, an
absolute HTTPS booking URL, local business windows, urgent-review behavior,
and zero through three follow-ups. Inbound answers persist tenant-bound
structured values; unresolved responses route to `NeedsHuman` and
`CriticalReview`. Qualification, booking, and follow-up actions use durable
stage/version idempotency, move outside-hours work to the next permitted
window, and re-check workflow, tenant, Lead, opt-out, reply baseline, template,
and send-count eligibility at execution. Authorized dashboard operators can
queue/cancel visible actions, and booking or closure cancels remaining
automated work. No AI or calendar provider was added.

## Epic E7 - AI assistance

### LR-0701 Structured analysis adapter

**Acceptance:**

- provider interface implemented;
- strict schema validation;
- timeout and retry bounded;
- minimum data sent;
- invalid output creates failure, not trusted suggestion.

Implementation note (2026-07-21): complete. Application owns the version 1.0
provider-neutral contracts and an exact-property validator. Infrastructure
uses a typed HTTP client for strict OpenAI Responses API JSON Schema output,
disables provider storage, masks phone/email values, caps recent context and
response size, and returns failure without a suggestion for refusal or invalid
output. Timeout is 1-30 seconds per attempt and only transient network/HTTP
failures receive zero through two retries. The adapter is disabled by default
and is not yet invoked or persisted; review UI and workflow fallback remain
LR-0702 and LR-0703.

### LR-0702 Human review UI

**Acceptance:**

- AI label shown;
- accept/edit/reject;
- correction audited;
- low confidence clearly marked;
- customer-facing action not automatic.

Implementation note (2026-07-27): complete. Tenant-owned analyses preserve the
original validated structured output and input-hash/category snapshot while a
separate terminal review stores accepted or corrected values, rejection,
optional evaluation reason, reviewer, time, and opaque concurrency version.
The Lead detail UI explicitly labels AI content, marks low confidence, exposes
accept/edit/reject only to dashboard operators, and labels suggested replies as
unsent drafts. Review actions are CSRF-protected, tenant-scoped, and redacted in
audit; they create no Message or ScheduledAction. Automatic invocation and
provider-outage fallback remain LR-0703.

### LR-0703 AI fallback

**Acceptance:**

- provider unavailable scenario tested;
- deterministic workflow continues;
- lead can be flagged NeedsHuman;
- no repeated costly retry storm.

## Epic E8 - Operations and security

### LR-0801 Observability

**Acceptance:**

- structured logs and correlation IDs;
- traces across webhook -> job -> provider;
- core metrics exported;
- PII redaction test.

### LR-0802 Kill switch

**Acceptance:**

- global and tenant automation disable;
- queued sends suppressed/cancelled;
- inbound capture/dashboard remain available;
- runbook tested.

### LR-0803 Retention job

**Acceptance:**

- dry-run mode;
- tenant policy applied;
- deletion archived/audited as required;
- no deletion across wrong tenant;
- restore/backup warning documented.

### LR-0804 Rate limiting/security headers

**Acceptance:**

- policies configured;
- tests for login/manual send;
- secure headers verified;
- provider webhooks not accidentally blocked under normal retry burst.

## Epic E9 - Deployment

### LR-0901 Production Docker images

**Acceptance:**

- multi-stage;
- non-root;
- health probes;
- image metadata/version;
- scan passes or exceptions documented.

### LR-0902 Kubernetes base

**Acceptance:**

- API, worker, web deployments;
- services and ingress;
- config/secret references;
- probes and resources;
- migration job;
- deployment works in local/staging cluster.

### LR-0903 CI/CD

**Acceptance:**

- PR pipeline quality gates;
- release images immutable;
- staging deploy and smoke test;
- production approval gate;
- rollback documented and tested.

## Epic E10 - Pilot readiness

### LR-1001 Tenant onboarding flow

**Acceptance:**

- configure business, phone, hours, templates, booking, users without code changes;
- validation prevents incomplete activation;
- onboarding checklist completed.

### LR-1002 Demo tenant and script

**Acceptance:**

- fictional data only;
- two-minute missed-call flow reproducible;
- duplicate and opt-out proof available;
- screenshots/README prepared.

### LR-1003 Pilot measurement

**Acceptance:**

- baseline fields defined;
- dashboard/report export available;
- success criteria agreed;
- no unsupported revenue claim.
