# Definition of Done

A task is done only when all applicable items are complete.

## Requirements

- [ ] Acceptance criteria satisfied.
- [ ] No unrelated scope added.
- [ ] Assumptions documented.
- [ ] API/schema behavior documented if changed.

## Code

- [ ] Readable names and structure.
- [ ] No secrets or hard-coded tenant/provider data.
- [ ] Cancellation tokens and async I/O used appropriately.
- [ ] Tenant isolation preserved.
- [ ] Errors handled explicitly.
- [ ] Logs are structured and PII-safe.

## Tests

- [ ] Unit tests added/updated.
- [ ] Integration tests added where needed.
- [ ] Critical path E2E updated where needed.
- [ ] Duplicate/retry behavior tested for integrations.
- [ ] Security/authorization tests added where needed.
- [ ] All relevant tests pass.

## Quality

- [ ] Formatting/lint passes.
- [ ] Build passes with required warning policy.
- [ ] Dependency/security scan reviewed.
- [ ] No debug bypass or disabled validation remains.

## Operations

- [ ] Health/telemetry impact considered.
- [ ] Configuration documented.
- [ ] Migration and rollback considered.
- [ ] Feature can be disabled safely when applicable.

## Documentation

- [ ] Relevant docs updated.
- [ ] Changelog/decision record updated if needed.
- [ ] Commands and test results included in task report.
