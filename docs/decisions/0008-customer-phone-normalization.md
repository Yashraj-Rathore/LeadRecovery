# ADR-0008: Customer phone normalization and identity

- Status: Accepted
- Date: 2026-07-13

## Context

LR-0202 requires equivalent phone formats to resolve to one customer within a
tenant while invalid or unknown numbers fail explicitly. Hand-written parsing
rules are incomplete and age poorly as numbering plans change. Phone numbers
also identify tenant-owned personal records, so request-supplied tenant IDs and
global uniqueness are both unsafe.

## Decision

Application code depends on `IPhoneNumberNormalizer`, which returns either a
canonical E.164 value or a typed failure. Infrastructure implements the port
with the centrally pinned `libphonenumber-csharp` package. International input
may omit a default region; national input requires a supported region. The
adapter rejects parse failures, impossible numbers, and invalid numbers before
persistence.

Customer creation derives `TenantId` only from the active server context. The
database stores canonical `PhoneE164` values and enforces a unique
`(TenantId, PhoneE164)` index, so equivalent formatting cannot create duplicate
customers inside one tenant while the same person may contact multiple tenant
businesses independently. Customer reads use a tenant query filter and the save
pipeline rejects missing, mismatched, or changed tenant ownership.

No raw phone input is logged by this workflow. The normalization dependency is
kept out of Domain and Application so it can be upgraded or replaced without
changing business policies.

## Consequences

Customer identity is deterministic within each tenant and invalid phone input
has an explicit application result. Callers must provide a default region for
national-format input. Numbering-plan behavior follows the pinned metadata and
requires normal dependency updates over time. LR-0102 remains open to extend
the same tenant query/write protections to the other tenant-owned entities as
their persistence is implemented.

Implementation follow-up (2026-07-29): the final sentence records the state at
ADR acceptance. LR-0203, LR-0204, LR-0103, and later authenticated feature
endpoints subsequently completed the equivalent LR-0102 protections.
