# ADR-0011: Identity, tenant membership, and browser session

- Status: Accepted
- Date: 2026-07-14

## Context

LR-0103 requires Owner and Staff authentication, tenant roles, secure session
cookies, logout invalidation, and authorization tests. The product documents
prefer same-origin browser sessions but did not define the Identity storage
model, tenant selection, cookie revalidation, CSRF boundary, or how a tenant
role differs from future platform support access.

## Decision

ASP.NET Core Identity stores `ApplicationUser` records with `Guid` keys and
owns password hashing, lockout, and security stamps. `TenantMembership` is an
explicit tenant-owned grant from a user to exactly one of Owner, Manager,
Staff, or ReadOnly. PlatformAdmin is not a tenant role; later support access
requires a separate time-bounded, audited grant.

Milestone 2 issues a non-persistent Identity application cookie only when the
user is active and has exactly one membership whose tenant is Trial or Active.
Zero or multiple eligible memberships fail closed until an explicit tenant
switcher is designed. The cookie contains the selected tenant and role, but
every request revalidates the user, security stamp, exact membership/role, and
tenant status against PostgreSQL. Browser requests cannot select or override
TenantId.

Next.js and the ASP.NET Core API share a browser origin through an `/api`
rewrite. The browser receives no bearer token. Session and antiforgery cookies
are HttpOnly and SameSite=Strict; production cookies are Secure and default to
`__Host-` names. Login and logout require the antiforgery request token, while
login also uses Identity lockout, generic failure text, and an IP fixed-window
rate limit. API authentication/authorization failures return `401`/`403`
instead of redirects.

Logout writes an audit event, rotates the user's security stamp, and clears the
cookie. Rotation invalidates every previously issued cookie for that user,
including replay of the just-cleared session. Successful login and logout are
recorded with correlation IDs and no secrets. Production deployments persist
data-protection keys in configured protected shared storage.

## Consequences

Tenant authority is deterministic and server-derived, and membership or tenant
revocation takes effect on the next request. Logout has a wider blast radius
than one browser because all user sessions are invalidated; this is an accepted
security-first Milestone 2 tradeoff. Multi-tenant account switching,
fine-grained support grants, password recovery, and persistent login require
separate later designs. All browser mutations must continue using antiforgery
validation, and every tenant endpoint must retain entity-level tenant scoping
even when a role policy has already passed.
