# ADR-0004: Transactional background work

- Status: Accepted
- Date: 2026-07-13

## Context

A PostgreSQL business transaction cannot safely promise atomic completion with
a separate Hangfire enqueue operation. Direct enqueue after a commit can fail,
while enqueue before commit can expose work whose business state later rolls
back.

## Decision

`ScheduledAction` is the durable application intent for deferred business work.
The transaction that changes a lead also creates or updates the corresponding
scheduled action. After commit, the application may notify Hangfire. A recurring
dispatcher also discovers pending due actions, so a notification failure cannot
lose work.

A Hangfire job carries only the scheduled-action ID and tenant context. Before
any external side effect, the worker reloads current state and verifies:

- the action is still pending and due;
- the lead remains eligible;
- automation and opt-out policies allow the action;
- the idempotency key has not already produced the business effect.

Status transitions and attempt records are persisted. External effects remain
at-least-once attempts and adapters use provider-supported idempotency where
available.

## Consequences

PostgreSQL remains the source of truth and Hangfire provides execution and
retry mechanics. No message broker, Redis, or distributed transaction is
required. Milestone 1 persists scheduled actions without running them;
Hangfire execution begins only in its assigned milestone.
