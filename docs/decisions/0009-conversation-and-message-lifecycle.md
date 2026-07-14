# ADR-0009: Conversation and message lifecycle

- Status: Accepted
- Date: 2026-07-14

## Context

LR-0203 requires inbound and outbound persistence, delivery-state validation,
provider and client idempotency constraints, and a body-length policy. The
earlier specification listed fields and statuses but did not define the exact
transition graph or length. Conversation and Message also require a persisted
Lead parent so PostgreSQL can enforce tenant-safe relationships.

## Decision

Conversations start `Open`, may transition once to `Closed`, and do not reopen
without a future explicit audited use case.

Inbound messages start and remain `Received`. Outbound messages start `Queued`.
The allowed outbound transitions are `Queued -> Sent -> Delivered`,
`Queued/Sent -> Failed`, and `Queued -> Suppressed`. `Received`, `Delivered`,
`Failed`, and `Suppressed` are terminal. Future callback handlers make repeated
events idempotent by comparing the persisted state and event identity before
invoking a transition; the aggregate rejects impossible regressions.

Every message has a required opaque `ClientIdempotencyKey`, unique within its
tenant. An inbound adapter derives this server-side; browser or webhook fields
never select tenant authority. `(Provider, ProviderMessageSid)` is globally
unique when the SID is present because the provider defines that identity
scope. Provider SIDs remain nullable for outbound messages until accepted by a
provider.

Message bodies preserve their exact content, reject whitespace-only values, and
are limited to 1,600 UTF-16 code units as a conservative application check for
the supported 1,600-character ceiling on incoming and outgoing Twilio
Programmable Messaging bodies. Shorter product copy remains preferable, and
provider encoding may impose additional segment costs.

LR-0203 adds Lead EF persistence as the minimum required parent for Conversation
and Message. Lead, Conversation, and Message use server-derived tenant query and
write guards. PostgreSQL uses compound tenant foreign keys so a child cannot be
linked to a parent from another tenant. This issue does not introduce provider
calls, webhook handlers, feature API endpoints, authentication, or background
execution.

## Consequences

Delivery state cannot regress silently, duplicate client actions cannot create
multiple messages within a tenant, and provider callbacks cannot attach one
provider identity to multiple records. The model is ready for later Twilio and
worker use cases without performing external side effects in Milestone 1.
