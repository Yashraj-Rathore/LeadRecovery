# ADR-0015: Deterministic qualification, booking, and follow-up

- Status: Accepted
- Date: 2026-07-21
- Owners: LeadRecovery engineering

## Context

Milestone 6 must collect tenant-specific answers, schedule customer contact in
local permitted hours, offer a booking path, and stop follow-ups reliably. The
requirements deliberately avoid making AI or a calendar provider responsible
for workflow correctness. They also leave DST conversion, ambiguous answers,
booking-link identity, and the maximum cadence needing explicit decisions.

## Decision

1. Each tenant has at most one active, versioned `WorkflowDefinition`.
   Validated JSON policies contain one through ten ordered RequiredText or
   Choice questions, at least one local business-hours window, an urgent-review
   after-hours flag, an approved absolute HTTPS booking URL without embedded
   credentials, and zero through three uniquely ordered follow-ups.
2. `QualificationEvaluator` is deterministic. Required text accepts a trimmed
   bounded value. Choice accepts an exact or single contained approved value;
   zero matches is Unknown and multiple matches is Ambiguous. Every result is
   stored as a tenant-bound `QualificationAnswer`. Unknown and Ambiguous move
   the Lead to `NeedsHuman`, set `CriticalReview`, cancel pending automation,
   and audit the policy-derived review timestamp. No AI call occurs.
3. Business hours use the tenant `TimezoneId`. Work already inside a configured
   half-open `[open, close)` window keeps its instant; otherwise it moves to the
   next opening. Spring-forward invalid local times advance to the first valid
   minute. Fall-back ambiguous times select the larger offset, the earliest UTC
   occurrence. Urgent human review may bypass send hours only when configured.
4. Qualification, booking, and follow-up work is durable `ScheduledAction`
   intent. Idempotency includes tenant scope implicitly plus Lead, workflow
   version, stage, question, or sequence. A Pending action may be deferred
   without incrementing its attempt count. The Worker re-checks the active
   workflow, tenant automation, Lead status/automation, opt-out, route,
   customer-activity baseline, approved template, stage, and cadence limit
   immediately before a send.
5. A booking action renders only the active approved `BookingLink` template and
   the validated workflow URL. The dashboard never accepts a URL in the queue
   request. Staff may mark the Lead Booked through the existing transition;
   that transaction cancels all pending automated actions. A calendar adapter
   remains a later optional integration.
6. Owner, Manager, and Staff may queue a booking link or cancel a visible
   Pending action. Both operations retain tenant query filters, entity
   ownership checks, CSRF protection, audit rows, and not-found behavior for
   cross-tenant identifiers. Booking queueing also uses the Lead concurrency
   token.

## Consequences

- The core qualification and booking flow continues when AI or calendar
  providers are unavailable.
- Policy JSON is versioned configuration, not executable user-authored code.
- Only one window per weekday is supported in this milestone; split shifts
  require a future policy version.
- Human notification is represented immediately in durable dashboard state and
  redacted audit data. Email delivery remains a separate future adapter.
- Provider execution remains at-least-once, while action and Message identities
  prevent ordinary duplicate business effects.
