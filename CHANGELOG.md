# Changelog

All notable repository and product-specification changes are recorded here.

## [Unreleased]

### Added

- Milestone 0 modular-monolith solution with API, worker, layered class
  libraries, and four test projects.
- Process liveness and readiness endpoints with integration coverage.
- Project-reference architecture guardrail.
- PostgreSQL Docker Compose service with persistent local storage and health
  check.
- Centrally pinned .NET package versions, locked restore, shared compiler and
  analyzer settings, local tool manifest, and backend CI quality gates.
- Reserved frontend and deployment directories without implementing future
  milestones.
- Architecture decisions for project boundaries, technology versions, tenant
  isolation, background consistency, API concurrency, lead lifecycle, tenant
  context, and canonical customer phone identity.
- LR-0101 Tenant aggregate with validation, PostgreSQL EF Core mapping, initial
  migration, and application-managed optimistic concurrency.
- Server-derived HTTP tenant context that accepts only a trusted tenant claim
  and fails closed when that claim is missing or invalid.
- PostgreSQL Testcontainers integration coverage for migrations, persistence,
  concurrency, tenant context, and database readiness.
- LR-0201 Lead aggregate with required fields and enums, UTC and identity
  invariants, explicit lifecycle transitions, required unsuccessful close
  reasons, and terminal-state protection.
- Booking application use case and cancellation port that complete lead
  automation and cancel pending workflow actions without introducing
  ScheduledAction persistence before LR-0204.
- LR-0202 Customer aggregate, server-derived customer creation use case,
  Infrastructure-isolated E.164 phone normalization adapter, and explicit
  invalid-number results.
- Customer PostgreSQL mapping and migration with tenant query/write guards,
  tenant-scoped canonical-phone uniqueness, and integration coverage for
  equivalent formatting, duplicate prevention, and cross-tenant isolation.

### Changed

- Clarified that `Booked` and `ClosedWon` are statuses, not close reasons.
- Replaced SQL Server-style row-version wording with an application-managed
  PostgreSQL `bigint Version` exposed as an opaque base64 API token.
- Required compound tenant foreign keys for tenant-owned relationships.
- Defined provider event identity so legitimate webhook status progression is
  not mistaken for duplicate delivery.
- Aligned Milestone 1 with the backlog by including Conversation,
  ScheduledAction, and ExternalEventReceipt persistence while deferring
  authentication, UI, Twilio, and Hangfire execution.
- Mounted the PostgreSQL 18 data volume at `/var/lib/postgresql` to support its
  major-version-specific data-directory layout.
- Resolved the lead lifecycle ambiguity so every pre-booking active state may
  route to human review or close unsuccessfully, while `Closed` and
  `ClosedWon` remain terminal until audited reopening is implemented.
- Pinned `libphonenumber-csharp` 9.0.34 centrally for canonical phone parsing
  while keeping the third-party API behind an application interface.
