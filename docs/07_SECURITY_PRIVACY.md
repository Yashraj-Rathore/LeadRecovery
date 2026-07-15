# 07 - Security, Privacy, and Compliance Design

## 1. Security objectives

Protect:

- tenant isolation;
- staff accounts;
- customer contact information and message content;
- Twilio, AI, email, and database credentials;
- integrity of automated messages;
- availability of the missed-call workflow;
- auditability of staff and support actions.

## 2. Threat model summary

Primary threats:

- forged webhooks;
- account takeover;
- cross-tenant data access;
- secret leakage;
- SMS abuse or unauthorized sends;
- injection through message content;
- prompt injection into AI analysis;
- duplicate/replayed webhooks;
- vulnerable dependencies or container images;
- excessive platform-admin access;
- data retained longer than necessary.

## 3. Authentication

- Use ASP.NET Core Identity or a well-supported identity provider.
- Passwords use platform-standard adaptive hashing.
- Require verified email before production access.
- Support MFA for Owners and PlatformAdmins before pilot expansion.
- Secure cookies: HttpOnly, Secure, SameSite appropriate to same-origin architecture.
- Session revocation on password reset and role removal.
- Login rate limiting and lockout controls.

## 4. Authorization

Roles:

- Owner
- Manager
- Staff
- ReadOnly
- PlatformAdmin

Owner, Manager, Staff, and ReadOnly are tenant membership roles. PlatformAdmin
is deliberately not a tenant role and is not implemented in Milestone 2; later
support access requires a separate time-bounded, audited grant model.

Authorization must check:

1. authenticated user;
2. active tenant membership;
3. required role/policy;
4. entity TenantId;
5. any special support-access grant.

Never rely only on UI hiding.

Milestone 2 uses ASP.NET Core Identity password hashing, unique normalized
emails, a 12-character complexity baseline, five-attempt/15-minute lockout,
generic authentication failures, and a separate five-attempt-per-minute IP
rate limit by default. Browser sessions are non-persistent HttpOnly
SameSite=Strict cookies. Production requires HTTPS and Secure `__Host-` cookies,
and data-protection keys must be persisted to protected shared storage.
Security stamps, users, exact membership roles, and Trial/Active tenant status
are revalidated for every request. Logout rotates the security stamp before
clearing the cookie, so replayed cookies are rejected immediately.

Login and logout require an antiforgery token returned by the same-origin CSRF
endpoint and sent in `X-CSRF-TOKEN`. The token cookie is HttpOnly,
SameSite=Strict, and Secure outside Development. Authentication redirects are
disabled for APIs: missing authentication returns `401`, insufficient role
returns `403`, and a cross-tenant entity lookup returns `404` without revealing
existence.

## 5. Tenant isolation controls

- active TenantId is server-derived;
- EF query filters plus explicit authorization checks;
- tenant-scoped unique keys;
- no mass assignment of TenantId;
- cross-tenant tests in CI;
- reports aggregate only within tenant unless a separate platform metric pipeline uses de-identified data.

LR-0202 applies these controls to Customer persistence: the server-derived
tenant context supplies ownership, EF filters reads, the save pipeline rejects
missing or mismatched tenant authority, and PostgreSQL enforces canonical-phone
uniqueness within each tenant. Equivalent guards must be added for each later
tenant-owned mapping under LR-0102.

LR-0203 extends the same controls to Lead, Conversation, and Message. Compound
tenant foreign keys reject cross-tenant relationships, client idempotency keys
are unique only within their tenant, and provider message identity is unique in
provider scope. Message bodies are never included in informational logs by this
slice.

LR-0204 extends tenant filters, write guards, compound Lead ownership, and
tenant-scoped idempotency to ScheduledAction. ExternalEventReceipt is a
system-level integration ledger instead: it may be written before tenant
resolution, is never exposed through tenant browser APIs, and permits TenantId
to move only from null to one resolved non-empty value. PostgreSQL uniqueness on
the full opaque provider-event identity prevents exact replay without
collapsing legitimate provider status progressions.

LR-0103 resolves browser tenant authority from the validated membership stored
in the session. Client-supplied tenant headers are ignored, lead list/detail
queries execute under the EF tenant filter, and integration plus Playwright
tests exercise cross-tenant denial in CI.

## 6. Webhook security

- validate Twilio signatures;
- use canonical public URL handling behind ingress;
- reject malformed payloads;
- persist idempotency receipt;
- limit accepted content length;
- acknowledge only after durable receipt;
- record correlation ID and provider SID;
- do not log full payload by default.

Milestone 3 uses the official pinned Twilio request validator and an
operator-configured canonical base URL rather than trusting inbound forwarded
headers. Validation happens before phone normalization or persistence. The auth
token is held only by the validator instance and is never passed to logging;
signatures, raw form values, and unmasked phone numbers are also excluded from
application logs and audit JSON. The public endpoint fails closed when either
the auth token or canonical URL is absent.

## 7. Input and output security

- server-side validation for all requests;
- parameterized queries through EF Core;
- output encoding in frontend;
- sanitize any rich text or avoid it entirely;
- treat SMS content as untrusted input;
- AI output is untrusted and schema-validated;
- prevent CSV formula injection in exports;
- limit file uploads because they are out of MVP scope.

## 8. Secrets management

Local development:

- `.env` or user-secrets, excluded from Git.

Staging/production:

- cloud secret manager or sealed external secret integration;
- Kubernetes Secrets only as delivery objects, encrypted at rest where supported;
- rotate credentials;
- separate credentials by environment;
- no secret values in Helm values committed to Git.

## 9. Encryption

- TLS for all external traffic;
- managed PostgreSQL encryption at rest;
- encrypted backups;
- optional application-level encryption for especially sensitive configuration values;
- do not create custom cryptography.

## 10. Logging and privacy

Never log:

- passwords;
- session cookies;
- bearer tokens;
- Twilio auth token;
- AI API key;
- full database connection strings;
- full message bodies at info level;
- unmasked phone numbers unless a restricted diagnostic mode is explicitly enabled.

Use structured fields such as:

- tenant ID;
- lead ID;
- message ID;
- provider SID hash or masked form;
- outcome;
- duration;
- correlation ID.

## 11. SMS consent and opt-out design

The initial use case is a response to a caller who contacted the business. The system must still:

- use tenant-approved wording;
- identify the business;
- include opt-out handling;
- suppress future automated messages after opt-out;
- log the contact source and consent basis;
- separate operational recovery messages from marketing campaigns;
- avoid importing marketing lists in MVP.

Legal requirements can vary. Pilot contracts should require the tenant to approve messaging practices and obtain appropriate legal advice.

## 12. Privacy design

- privacy notice identifies the business and service providers;
- collect minimal lead data;
- provide configurable retention;
- support tenant export/deletion requests;
- use data-processing agreements with providers where required;
- document where data is stored;
- do not use customer content for unrelated analytics or model training;
- minimize AI-provider input.

## 13. Support access

Platform support access must be:

- disabled by default;
- granted for a reason and limited period;
- least privilege;
- visible to tenant Owner where appropriate;
- fully audited;
- revocable immediately.

## 14. Dependency and supply-chain security

CI must include:

- NuGet vulnerability audit;
- npm audit or equivalent;
- dependency update automation;
- secret scanning;
- static analysis;
- container image scanning;
- software bill of materials for release images when practical;
- pinned base-image tags or digests for production.

## 15. Kubernetes security baseline

- non-root containers;
- read-only root filesystem where practical;
- drop unnecessary Linux capabilities;
- resource requests and limits;
- separate service accounts;
- no default service-account token mounting unless needed;
- NetworkPolicies in staging/production when supported;
- PodDisruptionBudget for multiple replicas;
- restricted ingress;
- namespace separation by environment;
- no public database service.

## 16. Incident response basics

Maintain runbooks for:

- leaked provider credential;
- unauthorized login;
- suspected cross-tenant exposure;
- unintended SMS broadcast;
- webhook outage;
- database restore;
- AI provider sending invalid output.

Every incident record includes time, scope, containment, remediation, customer communication decision, and preventive action.
