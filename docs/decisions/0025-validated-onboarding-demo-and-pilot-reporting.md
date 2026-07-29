# ADR 0025: Validated onboarding, demo evidence, and pilot reporting

- Status: Accepted
- Date: 2026-07-29

## Context

Milestone 10 requires repeatable tenant configuration, a truthful fictional demonstration, and useful pilot evidence without adding a platform-admin browser role or implying revenue attribution.

## Decision

A trusted operator provisions schema-versioned JSON through the API executable. Passwords are referenced by environment-variable name, validation is available without writes, and activation is one serializable transaction: Tenant status changes from Trial to Active only after users, memberships, phone, active workflow, and approved active templates persist. Automation defaults off.

Tenant members receive a read-only pilot report and CSV built from existing tenant-filtered Leads, Messages, Templates, and AuditEvents. Its missed-call denominator, date boundary, and field definitions are published. Booking requires staff confirmation, and every representation disclaims revenue and causal inference.

Demo media is generated from the opt-in fictional seed and real browser UI. The deliberately paced capture is separate from normal E2E tests. Duplicate and STOP captions link to named integration tests; media alone is not proof.

## Consequences

- New tenants can be configured without a deployment or code edit.
- A partial plan or Identity failure cannot leave an active tenant.
- Operator access to database configuration remains privileged and outside the customer UI.
- Reporting reads bounded operational records and is not analytics infrastructure.
- Seeded records and the fake SMS adapter demonstrate product behavior but do not validate a live carrier or market outcome.
