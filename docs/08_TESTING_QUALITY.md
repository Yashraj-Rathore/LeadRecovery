# 08 - Testing and Quality Strategy

## 1. Quality principle

The product controls customer communication and stores personal information. A visually working demo is not enough. Critical behavior must be repeatable, observable, and testable.

## 2. Test pyramid

### Unit tests

Focus on:

- lead state transitions;
- follow-up eligibility;
- business-hours calculation;
- cooldown rules;
- opt-out detection;
- phone normalization;
- conversation closure and message delivery-state transitions;
- message body limits and identifier invariants;
- scheduled-action transition matrix, retry timing, and terminal states;
- external-receipt tenant assignment and processing invariants;
- template rendering;
- AI-result validation;
- authorization policies;
- retry classification.

### Application tests

Test use cases with fakes:

- process missed call;
- process inbound SMS;
- queue/send recovery message;
- pause and resume automation;
- book/close lead;
- apply AI suggestion;
- cancel pending actions.

### Integration tests

Use real PostgreSQL through Testcontainers.

Test:

- EF mappings and migrations;
- tenant filters;
- transactions;
- unique/idempotency constraints;
- compound tenant foreign keys and tenant-owned write guards;
- scheduled-action tenant filtering, due/idempotency indexes, booking
  cancellation, and external-receipt identity/tenant immutability;
- Hangfire persistence where practical;
- authentication and cookies;
- API endpoints;
- webhook signature validation.

LR-0103 integration coverage uses the real PostgreSQL migration and API host to
verify Owner/Staff login, HttpOnly/SameSite/Secure cookie attributes, required
CSRF for login/logout, immediate logout-cookie replay rejection, audit rows,
generic invalid/suspended login failure, `401` for anonymous access, Owner-only
policy behavior, ignored tenant-header spoofing, and list/detail cross-tenant
denial.

LR-0301 through LR-0303 coverage uses an official-shaped Twilio form fixture,
independently computes the provider signature, and signs the configured public
URL while the test client uses its internal host. PostgreSQL integration tests
verify valid recovery creation, invalid-signature `403` with no receipt,
duplicate replay, cooldown, unknown-number acknowledgement without tenant
business data, and suspended-tenant suppression. Unit tests cover tenant-phone
policy normalization, lead activity updates, use-case scheduling, cooldown,
audit, metrics, and duplicate short-circuiting. No test uses a live provider or
sends SMS.

LR-0401 through LR-0405 add unit coverage for template approval, customer
opt-out, provider payload coordination, and transient retry signaling. The
PostgreSQL suite independently signs inbound and delivery forms and verifies
approved-template sending, duplicate-job suppression, STOP cancellation and
future-send blocking, permanent delivery failure visibility, callback
idempotency, and invalid-signature rejection. One integration test starts a
real Hangfire server with PostgreSQL storage and proves the queued worker job
reaches Completed. Every automated path uses the deterministic fake sender.

### Contract tests

- Twilio form payload fixtures;
- AI structured JSON fixtures;
- booking webhook fixtures;
- OpenAPI schema checks.

### End-to-end tests

Use Playwright for browser flows and a fake Twilio adapter by default.

Critical E2E scenarios:

1. Missed call creates one lead and one outbound message.
2. Duplicate missed-call callback creates no duplicate.
3. Inbound reply appears in timeline.
4. Staff pauses automation.
5. Staff sends a manual message.
6. Staff books lead and pending follow-ups are cancelled.
7. STOP suppresses automation.
8. User from Tenant A cannot view Tenant B lead.
9. Failed provider send appears in UI.
10. AI outage does not stop deterministic workflow.

The Milestone 2 Playwright slice signs into a seeded second tenant, captures a
lead identifier, signs out, signs into the first tenant, verifies the visible
lead sets, and confirms the second tenant's identifier returns `404`. CI builds
the production frontend, applies all migrations to isolated PostgreSQL, starts
the real API and Next.js shell, and runs this test in Chromium. Later prompts
add the remaining critical E2E scenarios as their provider and workflow
features become available.

Milestone 5 adds PostgreSQL integration coverage for CSRF-required dashboard
mutations, ReadOnly denial, active-member assignment, stale opaque row-version
conflicts, transition and pause audit rows, pending-action cancellation,
manual-message idempotency, fake-provider Worker completion, and opt-out
blocking. A 10,000-Lead tenant test measures ten warmed filtered HTTP reads and
requires p95 below 500 ms. Playwright verifies labeled filters and keyboard
focus, detail/timeline rendering, latest-state conflict recovery, pause state,
notes, manual SMS queue visibility, and cross-tenant denial.

Milestone 6 adds domain tests for workflow and structured-answer invariants;
unit tests for deterministic exact, single-match, ambiguous, and unknown
qualification outcomes; and tenant-timezone scheduler tests spanning both
Toronto DST changes. PostgreSQL tests apply the migration and verify structured
answer capture, urgent human routing, business-stage cancellation, approved
booking rendering once, the three-follow-up maximum, execution-time closure
suppression, CSRF, action cancellation, and cross-tenant denial. Playwright
extends the office flow through queueing the approved booking link and marking
the Lead booked, after which its pending automated booking action disappears.

LR-0701 adds application unit coverage for request bounds, exact schema
validation, approved-category enforcement, additional-property rejection, and
confidence/safety review policy. Fake-HTTP provider contract tests inspect the
strict Responses API request, `store: false`, recent-context limits, phone/email
masking, raw-tenant omission, transient retry cap, timeout, refusal, and invalid
output. No test calls a live AI provider or needs an API key.

LR-0702 adds domain coverage for immutable suggestions and terminal
accept/edit/reject transitions; Application validation coverage; and real
PostgreSQL/API tests for migration constraints, CSRF, operator authorization,
cross-tenant denial, optimistic review concurrency, redacted correction audit,
and the absence of Message/ScheduledAction side effects. Playwright verifies
the AI label, low-confidence warning, unsent-draft guardrail, staff correction,
and visible audited timeline result using fictional seed data.

LR-0703 adds Application tests for canonical input hashing, strict scheduled
payload parsing, ignored work, and one-call typed failure completion. Real
PostgreSQL integration tests independently sign inbound SMS requests and prove
that deterministic qualification commits before analysis; a provider outage
routes the Lead to `NeedsHuman`, records one terminal failed action, emits no
customer Message, and is ignored on duplicate execution. Success persists one
validated analysis without applying it, while consecutive inbound replies
cancel older Pending analysis work. Tests use an unavailable or in-process fake
provider and never call a live AI service.

LR-0803 adds domain bounds and Application orchestration coverage plus real
PostgreSQL proof that dry-run preserves data, delete mode removes only eligible
terminal Leads for the active policy tenant, recent and cross-tenant Leads
remain, and both modes append PII-free audit manifests. Runtime-option tests
require an explicit backup acknowledgement for destructive mode.

LR-0804 adds configured low-quota API hosts that prove login IP and authenticated
manual-send limits return `429`/`Retry-After`, verifies the API security-header
set, and submits 25 valid signed duplicate provider callbacks without a rate-
limit rejection or duplicate business effect.

## 3. Test environments

### Local

- Docker Compose PostgreSQL;
- fake providers or provider test credentials;
- seeded fictional data;
- no real sends by default.

The integration fixture starts Testcontainers by default. Environments where
Docker is unavailable may point
`LEADRECOVERY_TEST_DATABASE_CONNECTION_STRING` at a fresh disposable PostgreSQL
database; the fixture still applies migrations and runs the identical suite.
Never point this override at a shared or persistent database.

Safe local validation keeps real delivery disabled:

```powershell
$env:SMS_PROVIDER = 'fake'
$env:ALLOW_REAL_SMS = 'false'
dotnet test tests/LeadRecovery.Domain.Tests --no-build
dotnet test tests/LeadRecovery.Application.Tests --no-build
dotnet test tests/LeadRecovery.ArchitectureTests --no-build
dotnet test tests/LeadRecovery.IntegrationTests --no-build
```

The integration project may use its default disposable Testcontainer or the
documented fresh `LEADRECOVERY_TEST_DATABASE_CONNECTION_STRING` override. It
must never run against a shared or persistent database.

### CI

- isolated PostgreSQL container;
- fake external adapters;
- deterministic clock;
- no production secrets;
- parallel test safety.

### Staging

- dedicated Twilio test number/account configuration;
- allowlist of test destination numbers;
- realistic ingress and TLS;
- managed database;
- AI enabled only if test-data policy permits.

## 4. Test data

Use fictional names and numbers reserved for testing. Never copy real pilot messages into public fixtures.

Seed tenants:

- Alpha Plumbing - normal configuration;
- Beta HVAC - separate tenant for isolation tests;
- Suspended Tenant - access denial tests.

## 5. Time testing

Inject `TimeProvider` or an application clock. Test:

- tenant timezones;
- daylight-saving changes;
- after-hours scheduling;
- next-business-day follow-up;
- jobs delayed across midnight;
- cancellation before execution.

## 6. Idempotency tests

For each webhook type:

- same payload twice;
- same provider SID with different legitimate status;
- out-of-order status callbacks;
- replay after job completed;
- concurrent duplicate delivery.

Expected result: no duplicate business action.

## 7. Security tests

- invalid Twilio signature -> 403;
- missing CSRF token on protected browser mutation -> rejected;
- unauthorized role -> 403;
- cross-tenant ID enumeration -> 404/403 without data leak;
- SQL/script payload in SMS -> safely displayed as text;
- expired session -> reauthentication;
- secret patterns absent from logs;
- rate limits function without data loss.

The dashboard Playwright suite also verifies skip-link keyboard focus, 44-pixel
mobile filter and Lead-action targets, and absence of horizontal overflow at a
390-pixel viewport. Its timeline assertions are scoped and count-based so a
repeat run against a disposable seeded database still proves that a new note or
manual message appeared.

## 8. Performance tests

Before pilot:

- 20 webhook requests/second for 2 minutes;
- 100 concurrent dashboard reads;
- 10,000 leads in one tenant;
- background worker processing 1,000 scheduled actions in a controlled test.

Measure:

- p50/p95/p99 latency;
- error rate;
- DB connections;
- job lag;
- CPU/memory;
- duplicate rate.

## 9. Resilience tests

- Twilio adapter temporary timeout;
- permanent invalid-number error;
- database restart;
- worker restart mid-job;
- API replica restart;
- AI timeout/invalid JSON;
- email provider outage;
- booking webhook duplicate.

## 10. Code-quality gates

CI blocks merge when:

- formatting fails;
- build fails;
- tests fail;
- coverage drops below agreed thresholds for core projects;
- vulnerability scan finds unapproved high/critical issue;
- secret scanner finds a credential;
- OpenAPI breaking change is unreviewed;
- architecture test detects forbidden dependency.

Suggested initial coverage goals:

- Domain: 90% lines/branches for state rules;
- Application: 80% meaningful coverage;
- Overall backend: 70% minimum, with critical flows fully covered.

Coverage is not a substitute for scenario quality.

## 11. Manual release checklist

- run demo flow end to end;
- verify test number allowlist in staging;
- verify message templates;
- verify opt-out;
- inspect health dashboard;
- verify migration plan and backup;
- verify rollback image/tag;
- confirm no debug logging or test bypasses;
- confirm environment banner;
- obtain approval for production send.

## 12. Definition of done

Use `templates/definition-of-done.md` for every issue and milestone.
