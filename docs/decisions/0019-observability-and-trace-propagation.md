# ADR-0019: Observability and trace propagation

- Status: Accepted
- Date: 2026-07-27
- Decision owners: LeadRecovery maintainers

## Context

LR-0801 requires operators to connect an inbound webhook to durable background
work and any paid provider call without putting customer data in logs or metric
labels. ASP.NET request activities end before Hangfire dispatch, so ambient
context alone cannot cross the database-backed queue. Telemetry export must not
become a workflow dependency, and deployment must remain compatible with jobs
queued by the preceding release.

## Decision

1. API and Worker use W3C activity identifiers and PII-safe JSON console logs.
   API correlation IDs are server-derived; customer-provided correlation values
   are not trusted or reflected.
2. A newly scheduled action stores a bounded correlation ID and nullable W3C
   `traceparent`/`tracestate`. The Worker resumes valid stored context as a
   consumer span. Invalid or legacy context starts a safe new trace and does not
   block the action.
3. Existing four-argument Hangfire entry points remain callable for queued jobs.
   New dispatches use telemetry-aware entry points, preserving rolling-deploy
   compatibility without rewriting Hangfire storage.
4. ASP.NET Core, `HttpClient`, runtime, Npgsql, workflow, Twilio/SMS, and OpenAI
   meters/spans use OpenTelemetry. Traces and metrics export through OTLP only
   when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured. Export is observational;
   an unavailable collector cannot control or stop deterministic workflows.
5. Job metrics use bounded job-type/outcome dimensions. Paid-provider metrics
   add the server-derived tenant GUID for cost attribution. Customer-controlled
   values, contact details, message/prompt content, URLs, provider SIDs, error
   descriptions, and secrets are forbidden from log messages and telemetry
   labels.
6. The scheduled-action telemetry columns are additive and nullable. Rollback
   deploys the older binaries before dropping them. Reversal may discard trace
   continuity but does not change workflow state or tenant ownership.

## Consequences

- Operators can follow webhook, queued job, and provider work as one trace and
  correlate it with durable action state.
- Local development requires no collector and still receives structured JSON
  logs; production must configure an OTLP-compatible backend and its alerts.
- Tenant-level provider cost analysis is possible without high-cardinality PII.
- Adding a new provider or job type requires a bounded operation name and a PII
  review before it can emit tags.

## Alternatives rejected

- Passing trace context only as Hangfire arguments: database intent would not
  retain the evidence before dispatch, and redispatch/recovery could lose it.
- Logging message bodies or phone numbers for searchability: this creates an
  unnecessary privacy and retention burden.
- Requiring a collector for application startup or workflow execution:
  observability failure must not become a customer-workflow outage.
- Adding a broker solely for trace propagation: the modular monolith and
  PostgreSQL scheduled-action boundary already provide the durable handoff.
