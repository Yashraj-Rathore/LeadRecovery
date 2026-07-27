# Changelog

All notable repository and product-specification changes are recorded here.

## [Unreleased]

### Added

- Milestone 0 modular-monolith solution with API, worker, layered class
  libraries, and four test projects.
- Process liveness and readiness endpoints with integration coverage.
- Project-reference architecture guardrail.
- PostgreSQL Docker Compose service with persistent local storage and health
  check.
- Centrally pinned .NET package versions, locked restore, shared compiler and
  analyzer settings, local tool manifest, and backend CI quality gates.
- Reserved frontend and deployment directories without implementing future
  milestones.
- Architecture decisions for project boundaries, technology versions, tenant
  isolation, background consistency, API concurrency, lead lifecycle, tenant
  context, and canonical customer phone identity.
- LR-0101 Tenant aggregate with validation, PostgreSQL EF Core mapping, initial
  migration, and application-managed optimistic concurrency.
- Server-derived HTTP tenant context that accepts only a trusted tenant claim
  and fails closed when that claim is missing or invalid.
- PostgreSQL Testcontainers integration coverage for migrations, persistence,
  concurrency, tenant context, and database readiness.
- LR-0201 Lead aggregate with required fields and enums, UTC and identity
  invariants, explicit lifecycle transitions, required unsuccessful close
  reasons, and terminal-state protection.
- Booking application use case and cancellation port that complete lead
  automation and cancel pending workflow actions without introducing
  ScheduledAction persistence before LR-0204.
- LR-0202 Customer aggregate, server-derived customer creation use case,
  Infrastructure-isolated E.164 phone normalization adapter, and explicit
  invalid-number results.
- Customer PostgreSQL mapping and migration with tenant query/write guards,
  tenant-scoped canonical-phone uniqueness, and integration coverage for
  equivalent formatting, duplicate prevention, and cross-tenant isolation.
- LR-0203 Conversation and Message aggregates with explicit open/close and
  delivery-state policies, a 1,600-character message body limit, and terminal
  received, delivered, failed, and suppressed states.
- Lead, Conversation, and Message PostgreSQL mappings with tenant query/write
  guards, application-managed Lead concurrency, compound tenant foreign keys,
  provider SID uniqueness, tenant-scoped client idempotency, and timeline
  indexes.
- PostgreSQL integration coverage for inbound/outbound message persistence,
  duplicate identifiers, cross-tenant relationships, tenant filtering, missing
  tenant context, and stale Lead writes.
- Architecture decision for conversation lifecycle, message delivery states,
  message identity, and body-length policy.
- LR-0204 ScheduledAction aggregate with validated pending, running, retry,
  completion, failure, and cancellation transitions plus tenant-scoped
  idempotency and due-work indexes.
- PostgreSQL-backed booking cancellation that persists the booked Lead and
  cancels only that lead's pending scheduled actions through one scoped EF
  transaction.
- ExternalEventReceipt system ledger with an opaque provider-event identity,
  optional one-time tenant resolution, immutable resolved ownership, and
  processing-result policy.
- Combined additive messaging/workflow migration and PostgreSQL coverage for
  LR-0203 and LR-0204 persistence, uniqueness, cross-tenant denial, receipt
  progression, and durable booking cancellation.
- Architecture decision for scheduled-action execution state, durable booking
  cancellation, and external-receipt identity and tenant resolution.
- LR-0103 ASP.NET Core Identity users, explicit tenant memberships with Owner,
  Manager, Staff, and ReadOnly roles, per-request membership/session
  revalidation, and audited login/logout events.
- Secure same-origin session endpoints with HttpOnly SameSite=Strict cookies,
  antiforgery validation, generic login failures, lockout, IP rate limiting,
  security-stamp logout invalidation, and production data-protection key
  persistence support.
- Tenant-scoped lead list/detail queries and endpoints that derive TenantId
  exclusively from the authenticated membership and return not-found for
  cross-tenant identifiers.
- Minimal pinned pnpm/Next.js workspace with an accessible login screen,
  authenticated tenant lead inbox, empty/error states, and logout control.
- Pinned the patched PostCSS 8.5.10 transitive override used by Next.js to
  eliminate the reported CSS-stringification XSS advisory.
- Opt-in fictional demo seeding whose credentials and phone values must be
  supplied through configuration.
- PostgreSQL authentication/authorization integration coverage and Playwright
  browser coverage for login, logout, seeded lead visibility, and cross-tenant
  denial.
- ADR-0011 documenting browser session, tenant membership, role, CSRF, and
  session invalidation decisions.
- LR-0301 Twilio call-status request adapter using the pinned official SDK for
  exact public-URL and form signature validation, with fail-closed configuration
  and canonical proxy-path support.
- LR-0302 tenant-owned provider-number mapping with globally unambiguous
  destination routing, per-number recoverable statuses, initial delay, cooldown,
  and suspended/global/number-level automation suppression.
- LR-0303 serializable missed-call processing that atomically records an opaque
  idempotency receipt, creates or updates a lead, writes a pending
  `SendInitialRecoverySms` action, records a redacted audit event, and emits
  fixed-cardinality metrics without sending SMS.
- Official-shaped Twilio fixtures plus unit and PostgreSQL integration coverage
  for canonical proxy signatures, invalid signatures, duplicate replay,
  cooldown, unknown numbers, and suspended tenants.
- ADR-0012 documenting canonical request validation, provider-number recovery
  policy, unknown-number acknowledgement, event identity, transaction scope,
  and the no-live-send Milestone 3 boundary.
- LR-0401 PostgreSQL-backed Hangfire worker execution with structured
  correlation fields, bounded transient retries, duplicate-safe action/message
  identity, and five-minute stale-running recovery.
- LR-0402 approved active tenant message templates, execution-time eligibility
  checks, durable queued messages, a deterministic fake sender, and a live
  Twilio sender gated by `SMS_PROVIDER=twilio` plus `ALLOW_REAL_SMS=true`.
- LR-0403 signed inbound SMS ingestion with idempotent receipts, customer/lead
  association, durable conversation history, unknown-number policy, redacted
  dashboard audit activity, and fixed-cardinality metrics.
- LR-0404 STOP-family opt-out handling that atomically updates customer and lead
  suppression, cancels pending recovery SMS actions, blocks future sends, and
  records an audit event.
- LR-0405 idempotent delivery callbacks that map sent, delivered, failed, and
  undelivered states, expose permanent errors without blind retries, and emit
  delivery metrics.
- EF migration for tenant-scoped immutable message templates and their
  compound relationship to Messages, plus a real Hangfire/PostgreSQL worker
  integration test and end-to-end signed webhook coverage.
- ADR-0013 documenting worker ownership, at-least-once provider execution,
  provider safety gates, opt-out words, callback identity, and retry policy.
- LR-0501 tenant-scoped operational inbox with status, urgency, unassigned,
  current-user, and exact-assignee filters; urgent human-review ordering;
  explicit loading, empty, retry, and unread states; keyboard-accessible
  controls; and a measured 10,000-lead p95 acceptance test.
- LR-0502 lead detail with a deterministic call, SMS, system, and internal-note
  timeline; plain-text untrusted content rendering; eight-second refresh;
  new-activity announcements; and visible pending actions.
- LR-0503 authorized self/other assignment and domain-backed status transition
  endpoints with CSRF, opaque Lead row versions, PostgreSQL conflict detection,
  latest-state `409` responses, UI recovery messaging, and redacted audit rows.
- LR-0504 durable idempotent manual SMS queueing through `Message` plus
  `SendManualSms` ScheduledAction, Worker execution through the safe provider
  gates, execution-time opt-out checks, delivery/failure timeline state, a
  dedicated rate limit, and manual labels.
- LR-0505 audited pause/resume controls that cancel pending automated work and
  recreate only eligible future initial recovery intent without cancelling
  explicit manual messages.
- Tenant-owned `LeadNote` persistence and migration, operational fictional demo
  data, dashboard API contracts, production Next.js lead-detail UI, and
  Playwright coverage for filters, keyboard focus, conflicts, notes, messaging,
  automation controls, and tenant isolation.
- Cohesive dashboard visual and usability refresh with a tenant workspace
  header, attention-first queue, human-readable workflow labels, clearer
  timeline and action hierarchy, skeleton/global failure states, high-contrast
  focus treatment, reduced-motion support, and verified 390-pixel mobile use.
- ADR-0014 documenting dashboard authorization, concurrency, timeline
  projection, manual-message worker flow, polling, and resume eligibility.
- LR-0601 versioned tenant workflow definitions, deterministic qualification
  questions, structured answer persistence, and unknown/ambiguous routing to
  urgent human review without an AI dependency.
- LR-0602 tenant-timezone business-hours scheduling with explicit after-hours
  deferral, a separate urgent-review policy, and spring/fall DST coverage.
- LR-0603 approved absolute HTTPS booking destinations, one action per workflow
  version and lead stage, dashboard queue/cancel controls, and booked-state
  cancellation of remaining automated actions.
- LR-0604 policy-derived follow-ups capped at three, execution-time tenant,
  lead, opt-out, reply, stage, and template checks, worker dispatch, and
  dashboard visibility/cancellation.
- Qualification-answer and workflow-definition PostgreSQL persistence,
  migration, API/OpenAPI projections, fictional demo workflow, and unit,
  PostgreSQL integration, and Playwright booking-flow coverage.
- ADR-0015 documenting deterministic workflow policy, business-hours and DST
  semantics, booking-link identity, follow-up limits, and human-review routing.
- LR-0701 provider-neutral lead-analysis contracts, version 1.0 strict local
  schema validation, conservative confidence/safety review policy, and typed
  provider failures that never carry an invalid suggestion.
- Optional OpenAI Responses API adapter with strict JSON Schema output,
  `store: false`, bounded/redacted recent context, phone/email masking,
  per-attempt timeout, at most two transient retries, a 64 KiB response cap,
  PII-safe logs, and fake-HTTP contract coverage without a live provider.
- ADR-0016 documenting the structured-analysis boundary, current
  `gpt-5.6-sol` default, data minimization, failure classification, and the
  deferral of persistence, workflow invocation, and staff review to LR-0702/
  LR-0703.
- LR-0702 tenant-owned `AiAnalysis` persistence with immutable validated
  suggestions, input-hash deduplication, allowed-category snapshots, explicit
  pending/accepted/edited/rejected review state, and opaque optimistic
  concurrency.
- Staff-only, CSRF-protected AI review endpoints and Lead detail controls with
  clear AI/low-confidence labels, full structured correction, optional reasons,
  rejection, ReadOnly display, and explicit unsent-draft/customer-action
  guardrails.
- Redacted correction audits, fictional no-provider demo analysis, domain,
  Application, PostgreSQL/API authorization, and Playwright review coverage,
  plus ADR-0017 documenting the no-customer-side-effect review boundary.

### Changed

- Clarified that `Booked` and `ClosedWon` are statuses, not close reasons.
- Replaced SQL Server-style row-version wording with an application-managed
  PostgreSQL `bigint Version` exposed as an opaque base64 API token.
- Required compound tenant foreign keys for tenant-owned relationships.
- Defined provider event identity so legitimate webhook status progression is
  not mistaken for duplicate delivery.
- Aligned Milestone 1 with the backlog by including Conversation,
  ScheduledAction, and ExternalEventReceipt persistence while deferring
  authentication, UI, Twilio, and Hangfire execution.
- Clarified LR-0302 “without creating data” as no tenant business data: a valid
  unknown callback retains only the system receipt and redacted audit required
  by the accepted idempotency design.
- Mounted the PostgreSQL 18 data volume at `/var/lib/postgresql` to support its
  major-version-specific data-directory layout.
- Resolved the lead lifecycle ambiguity so every pre-booking active state may
  route to human review or close unsuccessfully, while `Closed` and
  `ClosedWon` remain terminal until audited reopening is implemented.
- Pinned `libphonenumber-csharp` 9.0.34 centrally for canonical phone parsing
  while keeping the third-party API behind an application interface.
- Added Lead persistence as the minimum LR-0203 prerequisite for database-
  enforced Conversation and Message ownership; no feature API or provider
  integration was introduced.
- Replaced the draft OpenAPI skeleton with the implemented authentication and
  read-only lead contract; future Twilio and dashboard-operation endpoints are
  no longer advertised as available.
- Expanded CI with the exact Node/pnpm toolchain, frozen frontend install,
  type-check/build/audit gates, migrations, and the PostgreSQL-backed
  Playwright acceptance test.
