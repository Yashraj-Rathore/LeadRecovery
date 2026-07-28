# ADR-0022: API rate limits and security headers

- Status: Accepted
- Date: 2026-07-28
- Decision owners: LeadRecovery engineering and security

## Context

LR-0804 requires tested limits for login and manual sends, consistent browser
security headers, and webhook protection that does not reject an ordinary
provider retry burst. Browser and provider traffic have different identities
and burst characteristics, so one global quota would couple unrelated work.

## Decision

Login uses a configurable IP-partitioned fixed window, default five requests
per minute. Manual SMS uses a tenant-and-authenticated-user fixed window,
default ten requests per minute; rate limiting therefore runs after
authentication. Twilio endpoints use a separate path-and-source token bucket
with 200-token burst capacity and 40-token-per-second refill. No requests queue
in-process. A rejected request returns `429` plus `Retry-After` when the limiter
provides it.

The API adds a response middleware that applies a JSON-API-compatible CSP
(`default-src 'none'`), frame denial, MIME sniffing prevention, no-referrer
policy, restrictive Permissions Policy, and cross-domain-policy denial to all
responses. Production HSTS and HTTPS redirection remain environment-gated.

## Consequences

- Login, staff-send, and each webhook path cannot consume one another's quota.
- Proxy/network configuration must supply the intended connection source
  address without trusting arbitrary client forwarding headers.
- Provider capacity permits short retries above the documented normal burst;
  sustained excess receives explicit backpressure without bypassing signature
  validation or idempotency.
- The strict API CSP is appropriate because this process serves JSON and health
  responses; the separately deployed Next.js application owns its document CSP.
