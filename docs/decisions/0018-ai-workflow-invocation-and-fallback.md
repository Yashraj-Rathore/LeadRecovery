# ADR-0018: AI workflow invocation and fallback

- Status: Accepted
- Date: 2026-07-27
- Decision owners: LeadRecovery maintainers

## Context

LR-0703 must invoke the optional structured-analysis adapter without making AI
the workflow controller. Inbound processing must remain reliable during an AI
outage, provider calls must not form a costly retry storm, and neither a
suggestion nor a failure may automatically send customer-facing content.

The existing transactional-background-work design already persists
`ScheduledAction` intent before Hangfire dispatch. LR-0702 already stores an
immutable, input-hash-deduplicated `AiAnalysis` and provides staff review, so a
new table, endpoint, broker, or service boundary is unnecessary.

## Decision

1. AI is disabled by default. API and Worker configuration must explicitly set
   `AI_ENABLED=true`. The active workflow must contain the configured Choice
   question (`AI_CATEGORY_QUESTION_KEY`, default `service`); its values are the
   tenant-approved category snapshot. Missing or invalid configuration creates
   no analysis action.
2. After an eligible signed inbound SMS is persisted and deterministic
   qualification is evaluated, the same transaction creates one immediately
   due `AnalyzeLead` action. Its strict payload snapshots the source Message,
   analysis schema, workflow identity/version, question key, and categories.
   A newer inbound reply cancels older Pending analysis actions for that Lead.
3. The Worker re-checks the tenant, automation, Lead status, customer opt-out,
   active workflow snapshot, and source Message before calling the provider.
   It builds at most eight sent/delivered/received turns ending at the source
   Message and computes a SHA-256 hash over the canonical provider-neutral
   request. Existing Lead/schema/input hashes complete without another call.
4. An analysis action permits one provider invocation at job level. Hangfire
   automatic retry is disabled. The adapter may still perform its ADR-0016
   zero-through-two transient retries within that invocation. If a running
   lease expires, reconciliation terminally records fallback without invoking
   the provider again.
5. A locally validated result creates at most one immutable `AiAnalysis` and
   completes the action. Suggestions are not copied into deterministic Lead
   fields. `RequiresHumanReview` may route an active Lead to `NeedsHuman`, but
   review remains an explicit staff decision under ADR-0017.
6. A typed provider, refusal, timeout, configuration, or validation failure
   terminally fails the action, stores only a normalized bounded failure code
   in action/audit data, and routes an eligible active Lead to `NeedsHuman`.
   The inbound Message and deterministic qualification are already committed
   and remain available.
7. Success and failure create no customer Message and enqueue no AI-generated
   reply. The separate manual-SMS path remains the only way staff can send an
   AI draft, with its existing authorization, opt-out, and provider gates.

## Consequences

- Provider outage cannot roll back or stop deterministic qualification, and
  staff receive durable fallback visibility.
- Burst replies coalesce before execution, canonical hashes suppress duplicate
  successful input, and lease recovery cannot repeat a costly provider call.
- API and Worker AI configuration should remain consistent. A mismatch fails
  safe: no API scheduling when disabled, and the Worker's unavailable adapter
  records fallback instead of making an unconfigured network call.
- One provider call may be lost if the process exits after the external call
  but before completion. The recovered action deliberately routes to human
  rather than risking a second billable call.
- No database migration or public API change is required for LR-0703.
