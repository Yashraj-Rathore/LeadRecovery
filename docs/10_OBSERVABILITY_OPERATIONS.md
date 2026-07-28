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
provider error body. LR-0703 analysis jobs add TenantId, ScheduledActionId,
CorrelationId, and a bounded workflow outcome to the existing structured
scope. Durable actions and redacted timeline audits expose created, failed,
cancelled, duplicate-skipped, and repeat-suppressed outcomes without message
content or input hashes. AI metrics and alerts remain LR-0801.

### LR-0801 implementation baseline

The API and Worker emit newline-delimited JSON console logs with UTC timestamps,
scopes, and W3C trace/span IDs. API requests receive a server-derived
`X-Correlation-ID`; untrusted client values are not reflected. Scheduled jobs
reuse that correlation ID in their structured scope. Phone numbers, email
addresses, message bodies, prompts, provider response bodies, API keys, and
authentication material are excluded from logs and telemetry tags.

The API creates W3C server spans for HTTP requests. A webhook-created
`ScheduledAction` stores a bounded correlation ID plus nullable `traceparent`
and `tracestate`. The Worker resumes the stored context as a consumer span and
creates child spans for real Twilio SMS sends and OpenAI analysis calls. The
three columns are nullable so actions queued before the LR-0801 migration keep
working; legacy Hangfire method signatures remain available during a rolling
deployment.

OpenTelemetry 1.17.0 exports traces and metrics over OTLP when
`OTEL_EXPORTER_OTLP_ENDPOINT` is an absolute HTTP or HTTPS collector URL. A
blank value disables network export while retaining JSON console logs. Both
processes attach service name, service version, and environment resources. Set
`LOG_LEVEL` to a named .NET log level to change the JSON console threshold; an
invalid value fails startup rather than silently changing visibility.

Exported instrumentation includes ASP.NET Core, `HttpClient`, runtime, Npgsql,
the existing Twilio webhook/SMS outcome meters, and the following bounded
operational instruments:

| Instrument | Type | Safe dimensions |
|---|---|---|
| `leadrecovery.jobs.executions` | counter | job type, outcome |
| `leadrecovery.jobs.duration` | histogram (seconds) | job type, outcome |
| `leadrecovery.jobs.queue_delay` | histogram (seconds) | job type |
| `leadrecovery.provider.requests` | counter | provider, operation, tenant ID, outcome |
| `leadrecovery.provider.duration` | histogram (seconds) | provider, operation, tenant ID, outcome |
| `leadrecovery.automation.actions_cancelled` | counter | automation scope, tenant ID |

The tenant dimension is a server-derived opaque GUID and is present only on
paid provider-call metrics, where cost attribution requires it. Do not add
Lead IDs, phone numbers, message text, URLs, provider SIDs, exception messages,
or other unbounded/customer-controlled values as metric labels.

Migration `AddScheduledActionTelemetryContext` is additive and requires no
downtime. Roll back application binaries before reversing it; dropping the
columns while new binaries are active will break scheduling, and reversing it
intentionally discards trace continuity for already queued work.

For an incident, begin with the response correlation ID, locate the matching
API JSON scope, then follow its trace through the scheduled-action consumer
span and provider child span. If OTLP export fails, workflows continue and the
JSON logs plus durable scheduled-action status remain the fallback evidence.

### LR-0802 kill-switch baseline

`AUTOMATION_GLOBAL_ENABLED` defaults to false and is read independently by the
API and Worker. Operators must set the same value in both processes. Global
disable is enforced on each dispatcher pass, cancels all pending automated
action types across tenants, and still permits manual staff SMS dispatch.
Tenant disable is a serializable Owner/Manager mutation of
`Tenant.AutomationEnabled`; it cancels that tenant's pending automated actions
in the same transaction and records a redacted audit. Every automated
scheduling and execution path rechecks global, tenant, Lead, and opt-out
eligibility. Inbound SMS/delivery callbacks and dashboard reads do not depend
on either automation switch.

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

1. Choose scope. For one tenant, an Owner or Manager opens the workspace-header
   control and selects **Pause tenant automation**. For a platform incident,
   set `AUTOMATION_GLOBAL_ENABLED=false` for both API and Worker and restart or
   roll out both processes.
2. Confirm the header reports `Tenant paused` or `Platform paused`. Check the
   `leadrecovery.automation.actions_cancelled` metric and Worker JSON log count;
   pending automated recovery, qualification, booking, follow-up, and analysis
   actions must be `Cancelled`.
3. Confirm any pending `SendManualSms` action remains pending/dispatchable.
   Manual staff callbacks and messages are intentionally not kill-switched.
4. Send a signed inbound test SMS to a non-production test Lead. Confirm it is
   persisted once and appears in Lead detail while no new automated action is
   queued. Confirm the inbox and Lead detail remain readable.
5. Record the incident/maintenance reason and affected tenants. Do not bypass
   Twilio signature validation or opt-out enforcement.
6. To recover one tenant, select **Resume tenant automation** after the cause is
   resolved. To recover globally, set `AUTOMATION_GLOBAL_ENABLED=true` in both
   processes and restart/roll out both. Verify the effective header state and a
   controlled new test event; cancelled work is not silently recreated.

The PostgreSQL integration acceptance tests execute this runbook boundary:
tenant disable cancels queued automation, stale/unauthorized writes fail,
signed-persistence use cases continue inbound capture, dashboard reads remain
available, and global disable preserves a pending manual action.

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
