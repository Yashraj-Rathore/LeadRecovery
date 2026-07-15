# ADR-0014: Operational dashboard and manual SMS

- Status: Accepted
- Date: 2026-07-15
- Owners: LeadRecovery engineering

## Context

Milestone 5 must turn the authenticated read-only shell into an operational
inbox without weakening tenant isolation, optimistic concurrency, opt-out
enforcement, or the API/worker separation established by earlier milestones.
The specifications also require a single ordered timeline even though call
activity, messages, internal notes, audits, and scheduled work have different
persistence models.

## Decision

1. Owner, Manager, and Staff memberships use a `DashboardOperator` policy for
   mutations. ReadOnly remains able to inspect tenant-owned leads but cannot
   assign, transition, pause, resume, add notes, or send messages. Every browser
   mutation validates the same-origin antiforgery token.
2. Assignment, status, pause, and resume requests carry the opaque Lead
   `expectedRowVersion`. Domain methods enforce legal state changes, PostgreSQL
   retains the `bigint` concurrency token, and stale writes return `409` with
   the latest safe lead representation.
3. `LeadNote` is a tenant-owned entity with a compound Lead foreign key and a
   same-tenant membership author. The detail timeline projects SMS records,
   notes, and redacted call/system audit activity into one deterministic order.
   The UI renders all bodies as plain React text and never executes HTML.
4. A manual send first persists a `Message` with `Kind=Manual` and a client
   idempotency key, then persists a `SendManualSms` ScheduledAction containing
   only its Message ID. The Worker re-checks tenant state, Lead policy,
   customer opt-out, provider number, and queued Message state before using the
   existing fake-or-explicitly-gated Twilio adapter. Delivery callbacks remain
   asynchronous.
5. Pausing changes the Lead to `PausedByUser` and cancels pending automated
   actions, never explicit manual-message intent. Resume is valid only from
   `PausedByUser` while tenant automation is operational. It creates a future
   initial-recovery action only for an eligible missed-call Lead with no prior
   automated Message and no pending/running recovery action; otherwise it
   safely creates none.
6. The inbox polls every ten seconds and an open Lead every eight seconds.
   Local composer text is not derived from refreshed server data. When activity
   arrives while the composer is focused, the UI announces it instead of
   changing the draft.
7. The inbox query applies status, urgency, assignment, and exact-assignee
   filters before paging, prioritizes urgent human-review work, and retains the
   documented PostgreSQL indexes. Integration acceptance seeds 10,000 tenant
   Leads and requires measured p95 HTTP latency below 500 ms.

## Consequences

- Manual SMS remains at-least-once with the same narrow provider crash window
  documented in ADR-0013, while database identity prevents ordinary duplicate
  sends.
- The timeline is a read projection rather than a new event-sourcing model.
- Live push, bulk operations, arbitrary reopening, follow-up cadence, booking
  links, category editing, and AI controls remain later issues.
- A worker must be running for a queued manual Message to progress from Queued
  to Sent; the dashboard exposes the queued and failure states while polling.
