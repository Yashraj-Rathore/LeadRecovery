# ADR-0020: Automation kill-switch scope and recovery

- Status: Accepted
- Date: 2026-07-28
- Decision owners: LeadRecovery engineering and operations

## Context

LR-0802 requires operators to stop automated customer work at platform or
tenant scope without losing inbound evidence or the staff workspace. The
existing tenant aggregate already stores `AutomationEnabled`, while the global
environment variable existed as an unused safe default. Manual staff messages
need a deliberate policy because cancelling every scheduled action would also
remove explicit human work.

## Decision

Automation is effective only when both `AUTOMATION_GLOBAL_ENABLED` and
`Tenant.AutomationEnabled` are true. Missing global configuration means false.
The global value is process configuration and must be coordinated across API
and Worker restarts; the tenant value is a transactional, optimistic-concurrent
Owner/Manager control.

Automated action types are initial recovery SMS, qualification questions,
booking links, follow-ups, and AI analysis. Disable prevents new scheduling and
rechecks eligibility immediately before execution. Tenant disable cancels its
pending automated actions in the same transaction. The Worker enforces global
disable by cancelling pending automated actions across tenants on each
dispatcher pass.

`SendManualSms` is outside the automation switch because it is explicit staff
intent and retains its opt-out/provider safety checks. Signed inbound SMS,
delivery callbacks, authentication, and dashboard reads remain available.
Cancelled work is not silently recreated on recovery; a new eligible event or
explicit existing domain workflow must create new intent.

Changes use fixed direction-appropriate reason codes, server-derived actor and
tenant identity, redacted audits, and a bounded cancellation metric.

## Consequences

- Platform disable requires a coordinated API/Worker rollout; a split value is
  visible in process behavior and must be treated as an incomplete operation.
- Safety does not depend only on queue cancellation because scheduling and
  provider preparation independently recheck eligibility.
- Operators can continue receiving and triaging customer replies while
  automation is paused.
- Recovery avoids surprise sends because previously cancelled actions remain
  terminal.
