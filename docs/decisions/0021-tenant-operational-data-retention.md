# ADR-0021: Tenant operational-data retention

- Status: Accepted
- Date: 2026-07-28
- Decision owners: LeadRecovery engineering and operations

## Context

LR-0803 requires dry-run reporting, tenant-specific retention, audited deletion,
cross-tenant safety, and an explicit backup/restore warning. Operational Lead
data contains contact and message content, while Customer opt-out state,
append-oriented audit evidence, and the provider idempotency ledger have
different compliance and safety purposes.

## Decision

Each Tenant stores an opt-in operational retention policy from 30 through 3,650
days, defaulting to disabled and 365 days. A daily UTC Hangfire maintenance job
runs only when `RETENTION_ENABLED=true`; its default mode is `dry-run`.
`delete` mode fails startup unless `RETENTION_BACKUP_CONFIRMED=true` is also
present. That flag is an operator acknowledgement, not proof that a usable
backup exists.

Only terminal `Closed` and `ClosedWon` Leads whose `ClosedAtUtc` is older than
the tenant cutoff are eligible. Work is bounded by `RETENTION_BATCH_SIZE`.
Deletion removes the selected Lead and its conversations, messages, notes,
qualification answers, scheduled actions, and AI analyses in one database
transaction. The same transaction appends a PII-free retention manifest with
mode, cutoff, policy, and aggregate counts. Dry-run appends the same manifest
without deleting data.

Every tenant batch uses an explicit trusted tenant execution scope, EF query
filters, and redundant TenantId predicates. A policy/scope mismatch fails
before mutation. Customers are retained so opt-out and consent state are not
lost; AuditEvents and ExternalEventReceipts are retained as compliance and
idempotency evidence. Their separate expiry policies remain future work.

## Consequences

- Tenant provisioning must deliberately enable and choose the policy; there is
  no tenant browser setting in LR-0803.
- Destructive execution is irreversible at application level. Recovery
  requires PostgreSQL backup/PITR restoration and may roll back unrelated work.
- The redacted manifest proves what category/count was evaluated or deleted but
  intentionally cannot reconstruct customer content.
- Repeated batches are safe and progressively drain eligible terminal Leads.
