# ADR-0012: Twilio call-status ingestion and recovery routing

- Status: Accepted
- Date: 2026-07-15
- Owners: LeadRecovery engineering

## Context

Milestone 3 needs to authenticate Twilio callbacks, resolve a tenant without
trusting request-supplied tenant data, distinguish callback progression from
duplicate delivery, and create durable recovery intent without sending SMS.
The product documents also require tenant-configurable recoverable statuses,
delay, and cooldown, but the general workflow-settings feature is not yet
implemented. `ExternalEventReceipt` is intentionally allowed to exist before
tenant resolution, while LR-0302 says unknown numbers must not create data.

## Decision

1. Add tenant-owned `TenantPhoneNumber` persistence as the narrow Milestone 3
   routing and recovery-policy boundary. It stores the recoverable status set,
   initial delay, and cooldown for the mapped number. A future settings feature
   may move these values into a versioned workflow definition.
2. Require global uniqueness for `(Provider, PhoneNumberE164)` as well as
   provider SID and tenant-phone uniqueness. A destination can therefore map to
   at most one tenant.
3. Treat Trial and Active tenants as operational only when tenant automation and
   number-level missed-call recovery are enabled. Suspended and Closed tenants
   are acknowledged without creating leads or scheduled actions.
4. Validate `X-Twilio-Signature` with the pinned official `Twilio` 7.14.9 SDK against a
   canonical public URL built from `TWILIO_WEBHOOK_BASE_URL` plus the request
   path/query. The configured base may include a trusted proxy path prefix. It
   must use HTTPS outside Development. Missing validator configuration fails
   closed with `503`; an invalid signature returns `403`.
5. Derive the opaque event identity from the Call SID and normalized status, so
   replay of one status is idempotent while legitimate status progression is
   retained. The payload hash covers sorted form fields.
6. Insert the receipt with `ON CONFLICT DO NOTHING`, resolve routing, update or
   create the lead, create a `SendInitialRecoverySms` scheduled action, and add
   a redacted audit event in one serializable PostgreSQL transaction. The
   trusted server-derived tenant scope remains active through commit.
7. A valid callback for an unknown destination creates only a system receipt
   and redacted integration audit event, then returns `204`. “Without creating
   data” in LR-0302 means no tenant business data: no lead or scheduled action.
8. Emit fixed-cardinality `System.Diagnostics.Metrics` counters for validation
   rejection and processing outcomes. Do not log the auth token, signature,
   payload, or phone numbers.
9. Milestone 3 persists pending recovery intent only. It does not execute
   Hangfire work or call Twilio's outbound API.

## Consequences

- Callback validation remains correct behind a configured reverse proxy without
  trusting arbitrary forwarded headers.
- Database uniqueness and a serializable transaction close duplicate and
  short-window cooldown races; a serialization failure is safe for provider
  retry because the receipt and business writes roll back together.
- Unknown valid callbacks leave a minimal system trace for replay control and
  operations while creating no tenant lead/message/action.
- Operators must configure both `TWILIO_AUTH_TOKEN` and
  `TWILIO_WEBHOOK_BASE_URL` before enabling the webhook.
- Outbound SMS, Hangfire execution, opt-out ingestion, and delivery callbacks
  remain Milestone 4.
