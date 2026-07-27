# ADR-0017: Human-reviewed AI analysis

- Status: Accepted
- Date: 2026-07-27
- Decision owners: LeadRecovery maintainers

## Context

LR-0702 requires staff to see, accept, edit, or reject AI suggestions while
preserving the rule that AI cannot control the workflow or send customer-facing
content. The preliminary `AiAnalysis` field list documented acceptance
metadata, but the backlog also requires edits, rejection, correction audit, and
clear low-confidence handling.

A review must retain the original validated output for evaluation, prevent
cross-tenant access, survive concurrent staff activity, and avoid copying
conversation summaries or draft replies into the audit ledger.

## Decision

1. A tenant-owned `AiAnalysis` stores the immutable validated suggestion,
   provider/model reference, schema version, allowed-category snapshot,
   SHA-256 input hash, confidence, review flag, reason codes, and structured
   output. `(TenantId, LeadId, SchemaVersion, InputHash)` is unique.
2. Review state is explicit and one-way:
   `Pending -> Accepted|Edited|Rejected`. A separate application-managed
   `bigint Version` provides an opaque review concurrency token.
3. Acceptance copies the immutable suggestion into reviewed values. Editing
   stores staff values separately and validates category against the analysis
   snapshot plus the reserved `Unknown` choice. Rejection stores no reviewed
   values. The original suggestion is never overwritten.
4. Owner, Manager, and Staff memberships may review. ReadOnly users may inspect
   the suggestion but receive `403` for writes. Session-derived tenant filters,
   compound tenant foreign keys, CSRF, and tenant membership revalidation apply
   to every review route.
5. Audit events store the reviewer, decision, analysis/Lead identity, changed
   field names, and whether a correction reason exists. They do not copy the
   summary, extracted customer data, suggested reply, or correction text.
6. The dashboard always labels AI-generated content, shows the original
   confidence, requires prominent review treatment below `0.65`, and labels a
   suggested reply as an unsent draft. Accept/edit/reject persist staff
   guidance only; they create no Message or ScheduledAction.
7. LR-0702 does not invoke the provider. The fictional demo seed supplies a
   pending low-confidence suggestion so the review UI is reproducible without
   an API key. Automatic workflow invocation, durable failure handling,
   `NeedsHuman` fallback, and retry-storm prevention remain LR-0703.

## Consequences

- Staff corrections and the original model output can be compared without
  ambiguity.
- A completed review is immutable; a future re-review feature would require a
  new audited decision or analysis rather than rewriting history.
- Category snapshots prevent a later workflow edit from invalidating the
  historical review choices, at the cost of small per-analysis JSON storage.
- Suggested replies remain operational drafts. Sending one still requires the
  separate explicit manual-SMS workflow and its existing policy checks.
