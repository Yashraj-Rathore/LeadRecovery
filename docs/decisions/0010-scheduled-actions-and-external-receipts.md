# ADR-0010: Scheduled actions and external receipts

- Status: Accepted
- Date: 2026-07-14

## Context

LR-0204 requires durable scheduled work, validated execution state, cancellation
when a lead is booked, and provider-event deduplication. Earlier documentation
did not define the exact action transition graph, retry accounting, transaction
boundary for booking cancellation, or how a receipt can exist before its tenant
is known.

## Decision

`ScheduledAction` is tenant-owned durable application intent. It begins
`Pending`. The allowed transitions are `Pending -> Running`,
`Pending -> Cancelled`, `Running -> Completed`, `Running -> Failed`, and
`Running -> Pending` for a retry whose new due time is not before the retry
decision. Starting increments `AttemptCount`; Completed, Failed, and Cancelled
are terminal. `(TenantId, IdempotencyKey)` is unique, compound tenant foreign
keys protect Lead ownership, and indexes support due-work selection and
lead-specific cancellation.

The LR-0201 booking port is implemented by a PostgreSQL adapter that uses the
same scoped EF DbContext as the tracked Lead. Its single save persists the
Booked transition and cancels only Pending actions for that tenant and lead.
Running and terminal actions remain unchanged. LR-0204 does not lease, dispatch,
or execute scheduled work and does not call Hangfire or any provider.

`ExternalEventReceipt` is a system integration ledger, not an ordinary
tenant-browser entity. It may be inserted with no TenantId before routing is
known. A non-empty TenantId may be assigned once and is immutable thereafter.
The ledger therefore has no tenant query filter and must not be exposed through
tenant browser APIs.

Receipt identity is the unique opaque tuple
`(Provider, EventType, ExternalEventId)`. An adapter-generated ExternalEventId
must distinguish legitimate provider status progressions; a provider object SID
alone is not necessarily an event identity. Processing may be recorded once,
at or after the receipt timestamp.

## Consequences

Booked leads and pending follow-ups cannot diverge because they are persisted
through one database transaction. Scheduled work has deterministic state and
idempotency before execution infrastructure is introduced. Exact provider-event
replays are rejected while legitimate progression remains representable.
System-ledger access requires explicit integration authorization in later
handlers because tenant query filtering is intentionally inapplicable.
