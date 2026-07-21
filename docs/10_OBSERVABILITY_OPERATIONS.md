# 10 - Observability and Operations

## 1. Objectives

Operators must be able to answer:

- Did we receive the provider webhook?
- Did it create/update the correct lead once?
- Was a recovery message queued, sent, and delivered?
- Are jobs delayed or failing?
- Is one tenant affected or the whole platform?
- Can automation be disabled safely?

## 2. Telemetry

Use OpenTelemetry-compatible instrumentation for:

- HTTP requests;
- EF Core/database calls;
- background jobs;
- external provider calls;
- custom workflow spans;
- trace propagation through queued work.

## 3. Structured logging

Common fields:

- timestamp;
- severity;
- service name and version;
- environment;
- correlation ID;
- trace/span ID;
- tenant ID;
- lead ID;
- job ID;
- provider event ID masked/hashed;
- outcome;
- duration.

Do not log full message content by default.

## 4. Metrics

### Platform

- API request count/error/latency;
- webhook request count/invalid signature rate;
- database latency and pool usage;
- job queue depth and oldest-job age;
- job success/failure/retry count;
- provider request latency/failure;
- pod CPU/memory/restarts.

### Business workflow

- missed calls detected;
- recovery messages queued/sent/delivered;
- inbound replies;
- opt-outs;
- leads needing human review;
- booked leads;
- average time to first response;
- automation suppressed count.

Business metrics must be tenant-scoped and access-controlled.

Milestone 4 emits fixed-cardinality counters from the
`LeadRecovery.Messaging.Sms` meter for outbound, inbound, and delivery outcomes.
Worker logs carry TenantId, ScheduledActionId, CorrelationId, and outcome in a
structured scope; they exclude phone numbers and message bodies. Milestone 5
projects durable Message, LeadNote, and redacted Lead AuditEvent records into
the polled tenant timeline. SignalR remains an optional later transport and is
not required for operational correctness.

Milestone 6 records redacted audit outcomes for booking queue/cancellation,
workflow deferral, suppression, provider completion, qualification result, and
the computed human-review timestamp. Payloads contain IDs, stages, enums, and
timestamps only; message bodies, phone numbers, and booking credentials are not
logged. Scheduled-action state and the Lead detail projection make every
pending workflow action visible and cancellable to authorized tenant staff.

LR-0701 emits structured provider success/failure logs with provider name,
model reference, attempt count, and a fixed bounded outcome. It never logs the
API key, prompt, conversation text, raw structured output, contact details, or
provider error body. AI metrics and alerts remain LR-0801.

## 5. Alerts

Initial alerts:

- invalid webhook signatures spike;
- no webhooks received for an active tenant during expected high-volume period (informational, not always outage);
- message failure rate above threshold;
- job lag over 5 minutes;
- API 5xx rate above threshold;
- database unavailable;
- production pod crash loop;
- AI invalid-output rate above threshold;
- opt-out handling job failure;
- backup failure.

## 6. Dashboards

### Operations dashboard

- service health;
- traffic/error/latency;
- job health;
- provider failures;
- deployment version;
- tenant-impact breakdown.

### Tenant dashboard

- operational lead funnel;
- response time;
- messages and failures;
- needs-human backlog.

## 7. Runbooks

### Runbook A - Outbound SMS failures

1. Check provider status and error codes.
2. Confirm credentials and account balance/configuration.
3. Check whether failures are tenant-specific or global.
4. Pause retry storm if permanent failure.
5. Keep leads visible for manual callback.
6. Notify affected tenant if impact is material.
7. Record incident and root cause.

### Runbook B - Webhook signature failures

1. Check canonical URL and forwarded-header configuration.
2. Confirm secret rotation.
3. Compare ingress public URL with provider configuration.
4. Reject invalid requests; do not temporarily bypass validation in production.

### Runbook C - Worker backlog

1. Inspect oldest job and common error.
2. Scale worker if work is healthy but capacity-limited.
3. Pause problematic job type if poison messages exist.
4. Preserve ordering/idempotency.
5. Verify recovery after change.

### Runbook D - Disable automation

1. Activate global or tenant kill switch.
2. Cancel pending automated-message actions.
3. Keep inbound message capture and dashboard available.
4. Confirm no sends for a test lead.
5. Inform tenant and record reason.

### Runbook E - Suspected tenant data exposure

1. Disable affected access paths.
2. Preserve logs and evidence.
3. Identify records/users/time window.
4. Rotate credentials if needed.
5. Escalate to incident owner and legal/privacy process.
6. Do not delete evidence.

## 8. Backups and recovery

- automated managed-database backups;
- point-in-time recovery where available;
- quarterly restore test before multiple paying tenants;
- documented RPO/RTO targets;
- encrypted backup storage;
- backup monitoring alert.

Initial pilot targets:

- RPO: 24 hours maximum, preferably lower with managed PITR;
- RTO: 8 hours for pilot, to be improved before SLA commitments.

## 9. Support model

Pilot support should define:

- support hours;
- urgent issue channel;
- severity definitions;
- response targets;
- planned maintenance process;
- client responsibility for business message approval and phone routing.

## 10. Cost observability

Track by environment and, where possible, tenant:

- SMS segments;
- phone numbers;
- AI requests/tokens;
- email sends;
- database/storage;
- compute;
- log ingestion.

Set budget alerts before production pilot.
