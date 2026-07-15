# ADR-0013: SMS worker and webhook lifecycle

- Status: Accepted
- Date: 2026-07-15
- Owners: LeadRecovery engineering

## Context

Milestone 4 must turn durable scheduled intent into an outbound SMS, ingest
customer replies and provider delivery state, honor opt-out immediately, and
remain safe under duplicate webhooks, job retries, and worker restarts. It must
also make local and automated execution incapable of accidentally sending a
real message.

## Decision

1. Run Hangfire servers only in `LeadRecovery.Worker`, with Hangfire 1.8.23 and
   Hangfire.PostgreSql 1.21.1 sharing the application PostgreSQL instance in a
   separate `hangfire` schema. API webhooks persist work but never host workers.
2. Dispatch only `SendInitialRecoverySms` actions. The job payload contains the
   server-derived tenant ID, action ID, and correlation ID; it contains no phone
   number or message body.
3. Lock the ScheduledAction in a serializable transaction, re-check tenant,
   route, lead, automation, booking, opt-out, and template eligibility, then
   persist the Customer association, open SMS Conversation, queued Message, and
   Running action before calling the provider.
4. Use `scheduled-action:{ActionId}` as the tenant-scoped message idempotency
   key. Duplicate job executions observe terminal action/message state and do
   not call the provider again. Work left Running for five minutes is returned
   to Pending for restart recovery.
5. Treat network, timeout, 429, and provider 5xx failures as transient. Return
   the action to Pending and let Hangfire retry after 30, 120, and 300 seconds.
   Treat other provider rejections as permanent, fail the Message and action,
   and do not create a blind retry from delivery callbacks.
6. Use a deterministic in-process fake sender by default. The Twilio sender is
   constructed only when `SMS_PROVIDER=twilio`, `ALLOW_REAL_SMS=true`, and both
   account credentials are configured.
7. Require an approved active `InitialMissedCallRecovery` template. Template
   body/version are immutable; one active template per tenant/purpose is
   enforced by PostgreSQL.
8. Validate inbound and delivery callback signatures against the configured
   canonical public URL before parsing business fields. Derive opaque receipt
   identities from Message SID for inbound and from Message SID, normalized
   status, and error code for delivery progression.
9. Recognize trimmed, case-insensitive `STOP`, `STOPALL`, `UNSUBSCRIBE`,
   `CANCEL`, `END`, and `QUIT`. Persist the inbound message, customer opt-out,
   lead suppression, pending-action cancellation, receipt, and redacted audit
   in one serializable transaction.
10. Emit structured worker logs and fixed-cardinality SMS outcome counters;
    never log phone numbers, credentials, signatures, or message bodies.

## Consequences

- The workflow is at-least-once. Database idempotency prevents normal duplicate
  execution, but a process crash after Twilio accepts a request and before the
  database records the SID remains the narrow external side-effect window.
  Operators reconcile that case using provider logs and the action correlation
  ID rather than automatically sending again without review.
- A worker restart cannot strand an action indefinitely, and permanent provider
  failures remain visible for staff follow-up.
- Inbound dashboard activity is durable in Message and AuditEvent records; the
  Milestone 5 UI/live notification transport may consume those records without
  changing webhook semantics.
- Operators must configure the two independent live-send gates deliberately.
