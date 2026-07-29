# Architecture decision records

These records capture decisions that resolve ambiguity across the product,
architecture, API, database, and milestone documents. An accepted ADR takes
precedence over older explanatory text; the modular specifications must be
updated in the same change so they remain aligned.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-modular-monolith-and-project-boundaries.md) | Modular monolith and project boundaries | Accepted |
| [0002](0002-pinned-technology-baseline.md) | Pinned technology baseline | Accepted |
| [0003](0003-tenant-isolation.md) | Tenant isolation | Accepted |
| [0004](0004-transactional-background-work.md) | Transactional background work | Accepted |
| [0005](0005-api-contract-and-concurrency.md) | API contract and concurrency | Accepted |
| [0006](0006-lead-lifecycle-and-webhook-identity.md) | Lead lifecycle and webhook identity | Accepted |
| [0007](0007-tenant-context-and-concurrency.md) | Tenant context and concurrency | Accepted |
| [0008](0008-customer-phone-normalization.md) | Customer phone normalization and identity | Accepted |
| [0009](0009-conversation-and-message-lifecycle.md) | Conversation and message lifecycle | Accepted |
| [0010](0010-scheduled-actions-and-external-receipts.md) | Scheduled actions and external receipts | Accepted |
| [0011](0011-identity-membership-and-browser-session.md) | Identity, tenant membership, and browser session | Accepted |
| [0012](0012-twilio-call-status-ingestion.md) | Twilio call-status ingestion and recovery routing | Accepted |
| [0013](0013-sms-worker-and-webhook-lifecycle.md) | SMS worker and webhook lifecycle | Accepted |
| [0014](0014-operational-dashboard-and-manual-sms.md) | Operational dashboard and manual SMS | Accepted |
| [0015](0015-deterministic-qualification-booking-and-follow-up.md) | Deterministic qualification, booking, and follow-up | Accepted |
| [0016](0016-structured-lead-analysis-adapter.md) | Structured lead-analysis adapter | Accepted |
| [0017](0017-human-reviewed-ai-analysis.md) | Human-reviewed AI analysis | Accepted |
| [0018](0018-ai-workflow-invocation-and-fallback.md) | AI workflow invocation and fallback | Accepted |
| [0019](0019-observability-and-trace-propagation.md) | Observability and trace propagation | Accepted |
| [0020](0020-automation-kill-switch.md) | Automation kill-switch scope and recovery | Accepted |
| [0021](0021-tenant-operational-data-retention.md) | Tenant operational-data retention | Accepted |
| [0022](0022-api-rate-limits-and-security-headers.md) | API rate limits and security headers | Accepted |
| [0023](0023-production-images-and-kubernetes-rollout.md) | Production images and Kubernetes rollout | Accepted |
| [0024](0024-immutable-cicd-promotion-and-rollback.md) | Immutable CI/CD promotion and rollback | Accepted |
| [0025](0025-validated-onboarding-demo-and-pilot-reporting.md) | Validated onboarding, demo evidence, and pilot reporting | Accepted |
| [0026](0026-openapi-contract-conformance.md) | Committed OpenAPI contract and executable route conformance | Accepted |

Use the next sequential number for a new decision. Do not rewrite the outcome
of an accepted ADR; supersede it with a new record and link both records.
