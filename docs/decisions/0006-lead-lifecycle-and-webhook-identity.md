# ADR-0006: Lead lifecycle and webhook identity

- Status: Accepted
- Date: 2026-07-13

## Context

The initial requirements mixed successful lifecycle states with reasons for
closing an unsuccessful lead. They also described provider IDs as idempotency
keys even though one provider object can legitimately emit several status
events.

## Decision

`Booked` and `ClosedWon` are lead statuses. They are not close reasons.
`Closed` is the terminal unsuccessful state and requires a documented reason in
the lost, duplicate, spam, or opt-out families. Booking stops pending follow-ups
and completes automation. A later staff-confirmed outcome moves `Booked` to
`ClosedWon`.

`ExternalEventReceipt.ExternalEventId` is an opaque value created by the
provider adapter. The unique key is `(Provider, EventType, ExternalEventId)`.
The adapter must include enough event identity to distinguish legitimate state
progression from redelivery; a call SID or message SID alone is insufficient
when the provider emits multiple event states for that object.

## Consequences

Reporting separates recovered/booked/won leads from lost leads without
overloading close reasons. Duplicate delivery has no duplicate business effect,
while legitimate provider status updates are not incorrectly discarded.
