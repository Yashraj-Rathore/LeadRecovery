# 11 - Implementation Timeline and Milestones

## 1. Planning assumptions

Baseline:

- one developer;
- 20-25 focused hours per week;
- reasonable familiarity with C#, TypeScript, Git, and APIs;
- new learning required for ASP.NET Core production patterns, Twilio, Docker/Kubernetes, and deployment;
- 10-week portfolio/pilot-ready schedule.

Do not compress by deleting testing, security, or documentation. Compress by reducing features.

## 2. Milestones

### Milestone 0 - Repository and decisions (2-3 days)

Deliverables:

- solution/repository structure;
- pinned toolchain versions;
- architecture decision log;
- formatting and analyzers;
- CI skeleton;
- Docker Compose PostgreSQL;
- health endpoint skeleton;
- documentation links;
- reserved `LeadRecovery.Web` path without initializing the Next.js application.

Exit criteria:

- clean checkout builds;
- tests run;
- local database starts;
- no feature code beyond skeleton.

### Milestone 1 - Domain, database, and tenant context (Week 1)

Deliverables:

- Tenant, Lead, Customer, Conversation, Message, ScheduledAction, and
  ExternalEventReceipt models;
- EF mappings and initial migration;
- tenant context abstraction;
- lead state-transition policies;
- unit and integration tests;
- seed data.

Authentication, ASP.NET Core Identity, tenant membership, roles, and the actual
Next.js application remain Milestone 2 scope.

Exit criteria:

- migration applies to empty PostgreSQL;
- domain state tests pass;
- cross-tenant query test passes.

### Milestone 2 - Authentication and dashboard shell (Week 2)

Deliverables:

- user authentication;
- tenant membership and roles;
- same-origin frontend/API setup;
- login and lead-list shell;
- authorization policies;
- audit-event foundation.

Exit criteria:

- Owner and Staff test users can log in;
- Tenant A cannot view Tenant B;
- frontend shows seeded leads.

Implementation status (2026-07-14): complete for the Prompt 3 acceptance
slice. Identity, tenant membership roles, audited secure sessions,
authorization policies, tenant-scoped lead reads, opt-in fictional seed data,
the Next.js login/inbox shell, PostgreSQL integration tests, and Playwright
cross-tenant coverage are present. The shell does not complete LR-0501 or
LR-0502: filters, assignments, full lead detail/timeline, messaging, and other
dashboard operations remain Milestone 6.

### Milestone 3 - Twilio missed-call ingestion (Week 3)

Deliverables:

- Twilio adapter and signature validation;
- call-status webhook;
- provider-number to tenant mapping;
- idempotent event receipt;
- lead creation/update;
- recovery scheduled action;
- webhook fixtures/tests.

Exit criteria:

- valid simulated missed call creates one lead/action;
- invalid signature rejected;
- duplicate callback creates no duplicate.

Implementation status (2026-07-15): complete for LR-0301 through LR-0303.
The signed call-status adapter, canonical proxy URL handling, globally unique
provider-number routing, tenant-specific recovery policy, serializable receipt
and business transaction, cooldown, audit, metrics, migration, and fixture-led
PostgreSQL tests are implemented. Pending actions are not executed and no live
SMS is sent; those remain Milestone 4.

### Milestone 4 - SMS and background worker (Week 4)

Deliverables:

- Hangfire storage and worker;
- outbound recovery SMS;
- inbound SMS webhook;
- message persistence;
- delivery status handling;
- opt-out suppression;
- retry classification;
- provider fake for tests.

Exit criteria:

- demo number receives SMS;
- reply appears in database/UI;
- STOP cancels actions;
- worker restart does not duplicate send.

Implementation status (2026-07-15): complete for LR-0401 through LR-0405.
PostgreSQL-backed Hangfire execution, deterministic fake and explicitly gated
Twilio providers, approved-template sending, execution-time eligibility,
signed inbound/status callbacks, STOP-family suppression, delivery-state
mapping, stale-running recovery, audits, metrics, and duplicate-safe
PostgreSQL integration coverage are implemented. The dashboard UI that renders
the durable inbound activity remains Milestone 5.

### Milestone 5 - Operational dashboard (Week 5)

Deliverables:

- inbox filters;
- lead detail timeline;
- manual message;
- assignment;
- status transitions;
- pause/resume automation;
- pending-action display;
- concurrency handling.

Exit criteria:

- office workflow can be completed entirely through UI;
- accessibility smoke test passes;
- E2E happy path passes.

Implementation status (2026-07-15): complete for LR-0501 through LR-0505.
The authenticated dashboard now provides filtered and measured tenant inbox
reads, ordered plain-text activity, pending actions, assignment, allowed domain
transitions, durable manual SMS, notes, audited pause/resume, and explicit
loading/error/concurrency behavior. PostgreSQL integration and Playwright cover
CSRF, role denial, stale writes, opt-out, worker completion, keyboard focus,
and the end-to-end office flow. Qualification, booking-link behavior, and
follow-up cadence remain Milestone 6.

### Milestone 6 - Qualification, booking, and follow-up (Week 6)

Deliverables:

- deterministic qualification questions;
- booking URL flow;
- follow-up cadence;
- business-hours scheduler;
- cancellation on booked/closed;
- staff notifications.

Exit criteria:

- qualification flow works without AI;
- no follow-up sent outside configured hours;
- booking transition cancels remaining jobs.

Implementation status (2026-07-21): complete for LR-0601 through LR-0604.
One active versioned tenant policy drives deterministic qualification,
timezone-aware permitted windows, the approved HTTPS booking link, and a
maximum of three follow-ups. Unknown or ambiguous answers route to urgent human
review without AI. The Worker re-checks eligibility at execution; dashboard
operators can queue the booking link, cancel pending work, and mark a Lead
booked to cancel remaining automation. Unit, PostgreSQL, and Playwright tests
cover DST, idempotency, tenant isolation, closure/opt-out suppression, and the
office booking flow.

### Milestone 7 - AI assistance and safety (Week 7)

Deliverables:

- provider abstraction;
- structured analysis schema;
- category/urgency/summary suggestions;
- human review UI;
- evaluation fixtures;
- timeout/fallback behavior.

Exit criteria:

- invalid JSON never reaches UI as trusted data;
- AI outage leaves core workflow working;
- low-confidence output requires review.

Implementation status (2026-07-27): complete for LR-0701 through LR-0703.
Application defines the provider-neutral request/result and strict schema
validator. The optional Worker uses a bounded OpenAI Responses API adapter with
redacted recent input, `store: false`, local output validation, typed failures,
a per-attempt timeout, and at most two transient adapter retries. Eligible
inbound replies create coalesced durable analysis work, validated suggestions
are deduplicated and presented for staff review, and provider outage routes the
Lead to `NeedsHuman` without undoing deterministic workflow state or sending a
customer-facing AI message. Unit, PostgreSQL, API, and Playwright coverage prove
the provider, persistence, review, fallback, and no-autonomous-send boundaries.

### Milestone 8 - Production hardening (Week 8)

Deliverables:

- observability;
- rate limiting;
- security headers;
- data retention job;
- performance tests;
- dependency/image scanning;
- runbooks;
- backup/restore documentation.

Exit criteria:

- release checklist passes;
- critical alerts configured in staging;
- security test suite passes.

### Milestone 9 - Docker, Kubernetes, and CI/CD (Week 9)

Deliverables:

- production container images;
- Kubernetes base and overlays;
- ingress and TLS configuration;
- health probes;
- migration job;
- GitHub Actions CI/CD;
- rolling-update demonstration;
- rollback test.

Exit criteria:

- staging deployment from clean pipeline;
- pod restart does not lose workflow state;
- secrets absent from repository;
- previous version can be restored.

### Milestone 10 - Pilot package and sales demo (Week 10)

Deliverables:

- fictional demo tenant;
- two-minute demo script/video plan;
- onboarding checklist;
- tenant configuration guide;
- pilot measurement plan;
- support and disable procedures;
- GitHub case-study README;
- architecture diagram and screenshots.

Exit criteria:

- another person can run the demo from documentation;
- pilot tenant can be configured without code changes;
- all known limitations documented.

## 3. First 14 days - daily plan

### Day 1

- create repository and solution;
- add `global.json`, editorconfig, analyzers;
- create projects and references;
- add basic CI build.

### Day 2

- Docker Compose PostgreSQL;
- configuration pattern;
- health endpoints;
- first architecture tests.

### Day 3

- implement Tenant and tenant context;
- create EF DbContext and base entity conventions.

### Day 4

- implement Lead entity and state transitions;
- write domain tests.

### Day 5

- implement Customer, Conversation, Message;
- add mappings and indexes.

### Day 6

- implement ScheduledAction and ExternalEventReceipt;
- initial migration;
- Testcontainers integration test.

### Day 7

- seed fictional tenants;
- review/refactor;
- update decisions and docs;
- milestone demonstration.

### Day 8

- configure Identity and TenantUser;
- create Owner/Staff roles;
- implement login endpoint/session.

### Day 9

- tenant authorization policies;
- cross-tenant endpoint tests;
- audit-event writer.

### Day 10

- initialize Next.js frontend;
- login screen;
- authenticated app shell.

### Day 11

- lead-list endpoint and typed frontend client;
- lead inbox UI with seed data.

### Day 12

- lead-detail endpoint;
- basic timeline UI.

### Day 13

- CSRF/session/security review;
- error/loading/permission states;
- E2E login and tenant-isolation tests.

### Day 14

- milestone review;
- fix defects;
- record short internal demo;
- prepare Twilio milestone.

## 4. Weekly time allocation

Suggested:

- 50% feature implementation;
- 20% automated testing;
- 10% architecture/documentation;
- 10% deployment/operations;
- 10% demo/customer validation.

During Weeks 1-4, do not spend more than 10% of build time on Kubernetes.

## 5. Scope-cut order if behind schedule

Cut in this order:

1. advanced reports;
2. SignalR, retain polling;
3. booking API integration, retain booking URL;
4. AI-generated reply drafts;
5. AI entirely, retain deterministic workflow;
6. multi-role refinements, retain Owner/Staff;
7. Helm, retain plain Kubernetes manifests.

Never cut:

- signature validation;
- idempotency;
- opt-out;
- tenant isolation;
- manual override;
- core tests;
- logs and kill switch.
