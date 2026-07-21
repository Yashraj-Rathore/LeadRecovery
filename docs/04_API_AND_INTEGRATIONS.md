# 04 - API and Integration Contracts

## 1. API conventions

Base path: `/api/v1`

- JSON request/response bodies.
- Problem Details for errors.
- Correlation ID returned in `X-Correlation-ID`.
- UTC ISO-8601 timestamps.
- Pagination with `pageSize` and opaque `cursor` for growing lists; offset pagination is acceptable for the first pilot if documented.
- Idempotency key supported for manual outbound-message requests.
- Browser API uses secure session authentication.
- Webhooks use provider-specific signature validation, not browser authentication.

## 2. Error shape

```json
{
  "type": "https://docs.example.com/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more fields are invalid.",
  "instance": "/api/v1/leads/123",
  "correlationId": "01J...",
  "errors": {
    "status": ["Transition from ClosedWon to AwaitingCustomer is not allowed."]
  }
}
```

## 3. Authentication endpoints

- `GET /api/v1/auth/csrf` issues an antiforgery request token and stores the
  paired HttpOnly SameSite=Strict cookie;
- `POST /api/v1/auth/login` requires `X-CSRF-TOKEN`, applies generic credential
  failure responses, Identity lockout, and an IP fixed-window rate limit;
- `GET /api/v1/auth/me` returns the validated user, tenant, and role session;
- `POST /api/v1/auth/logout` requires `X-CSRF-TOKEN`, rotates the Identity
  security stamp, clears the cookie, and invalidates replay of all previously
  issued cookies for that user.

The browser uses a non-persistent, HttpOnly, SameSite=Strict application cookie
with an eight-hour sliding lifetime. It is always Secure outside Development;
production defaults to a `__Host-` cookie and persists data-protection keys from
configured storage. The browser and `/api` share one origin through the Next.js
rewrite. TenantId is never accepted from request bodies, query strings, or
headers as authority. Password reset, refresh, tenant switching, and
PlatformAdmin support grants are deferred and are not advertised by the
implemented OpenAPI contract.

## 4. Lead endpoints

### List leads

`GET /api/v1/leads?pageSize=25&cursor=...`

The Milestone 5 endpoint returns tenant-scoped summary fields with assignment,
last activity, unread state, automation state, and opaque row version.
`pageSize` is 1 through 100 and `cursor` is an opaque encoded offset. Optional
`status`, `urgency`, `assignment=all|unassigned|mine`, and `assignedUserId`
filters are applied before paging. Human-review and urgent work sort first.

### Get lead

`GET /api/v1/leads/{leadId}`

The Milestone 5 endpoint returns the inbox summary plus a consistently ordered
plain-text timeline of call, SMS, system, and internal-note events; pending or
running actions; active tenant assignees; and domain-allowed transitions. It
returns `404` for an unknown ID and for an ID owned by another tenant. Polling
and conflict-refresh behavior are defined in the frontend specification. AI
suggestions remain a later milestone.

The dashboard write endpoints below are implemented and included in
`api/openapi.yaml`. They require an authenticated Owner, Manager, or Staff
membership and `X-CSRF-TOKEN`; ReadOnly receives `403`.

### Update lead status

`POST /api/v1/leads/{leadId}/transitions`

```json
{
  "targetStatus": "Booked",
  "reason": "Customer selected 2:00 PM appointment",
  "expectedRowVersion": "base64-version"
}
```

`expectedRowVersion` is an opaque base64 representation of the
application-managed `bigint` concurrency version. Do not expose arbitrary
patching of domain status.

### Assign lead

`POST /api/v1/leads/{leadId}/assignment`

The request carries nullable `assignedUserId` plus `expectedRowVersion`. The
target must be an active membership of the authenticated tenant; null unassigns.

### Pause automation

`POST /api/v1/leads/{leadId}/automation/pause`

### Resume automation

`POST /api/v1/leads/{leadId}/automation/resume`

### Add internal note

`POST /api/v1/leads/{leadId}/notes`

Assignment, transitions, pause, and resume return `409` with the current safe
Lead representation when the opaque expected row version is stale. Pause
cancels pending automated actions. Resume may recreate one future initial
recovery action only when the missed-call Lead and tenant remain eligible.

## 5. Message endpoints

- `GET /api/v1/leads/{leadId}/messages`
- `POST /api/v1/leads/{leadId}/messages`

Message state is returned in the lead timeline. A separate
`GET /api/v1/messages/{messageId}/status` route remains a future contract and is
not included in the Milestone 5 OpenAPI document.

Manual send request:

```json
{
  "body": "Thanks. A team member will call you shortly.",
  "idempotencyKey": "ui-01J..."
}
```

Server rules:

- verify user permission;
- verify tenant ownership;
- verify customer not opted out unless message is legally/operationally permitted;
- apply length and content validation;
- persist queued record before provider call;
- update delivery state asynchronously.

Milestone 5 queues manual messages as durable `Message` plus `SendManualSms`
ScheduledAction records before returning. The Worker resolves phone and body
from tenant-scoped persistence, re-checks opt-out and Lead policy, and uses the
same fake-by-default/live-explicitly-gated provider path as automated recovery.

## 6. Tenant configuration endpoints

- `GET /api/v1/settings/business`
- `PUT /api/v1/settings/business`
- `GET /api/v1/settings/messages`
- `POST /api/v1/settings/messages`
- `POST /api/v1/settings/messages/{id}/approve`
- `GET /api/v1/settings/automation`
- `PUT /api/v1/settings/automation`
- `GET /api/v1/settings/integrations`

Only Owner/Manager roles may edit configuration. Approval may require Owner depending on pilot contract.

## 7. Reporting endpoints

- `GET /api/v1/reports/overview?from=...&to=...`
- `GET /api/v1/reports/funnel?from=...&to=...`
- `GET /api/v1/reports/failures?from=...&to=...`

## 8. Twilio integration

### 8.1 Webhook endpoints

- `POST /api/v1/webhooks/twilio/voice`
- `POST /api/v1/webhooks/twilio/call-status`
- `POST /api/v1/webhooks/twilio/sms/inbound`
- `POST /api/v1/webhooks/twilio/sms/status`

Milestone 3 implements
`POST /api/v1/webhooks/twilio/call-status`. It accepts
`application/x-www-form-urlencoded` callbacks containing `CallSid`,
`CallStatus`, `From`, and `To` (with `Caller`/`Called` compatibility). A valid
callback returns `204` after durable processing; duplicate, unknown,
non-recoverable, cooldown, and inactive-tenant outcomes are also acknowledged
with `204`. Malformed signed input returns `400`, an invalid or missing
signature returns `403`, and missing validator/canonical-URL configuration
returns `503`.

Milestone 4 implements `POST /api/v1/webhooks/twilio/sms/inbound` and
`POST /api/v1/webhooks/twilio/sms/status` with the same signature and canonical
URL rules. Inbound events require `MessageSid`, `From`, `To`, and a non-empty
body of at most 1,600 characters. Delivery events require `MessageSid` and
`MessageStatus`, with optional `ErrorCode`. Accepted, duplicate, unknown, and
non-actionable signed callbacks return `204`; malformed, unsigned, and
unconfigured outcomes remain `400`, `403`, and `503` respectively.

### 8.2 Required controls

- Validate Twilio signature against the exact public URL and form values.
- Support proxy/ingress forwarded headers safely so signature validation uses the canonical URL.
- Reject invalid signatures with 403.
- Return 2xx quickly after durable receipt.
- Use provider SID plus event type for idempotency.
- Never trust tenant ID from webhook form fields.
- Resolve tenant through the called/messaged Twilio number.

The implemented canonical URL is built from the operator-controlled
`TWILIO_WEBHOOK_BASE_URL` plus the request path and query. Arbitrary forwarded
headers are not trusted. The base must use HTTPS outside Development. Unknown
destinations create only a system receipt and redacted audit event for replay
control; they create no tenant lead or scheduled action.

### 8.3 Recoverable call statuses

Tenant-configurable set, initially:

- no-answer
- busy
- failed

`completed` is not automatically recoverable without additional rules because a completed call may have been answered.

### 8.4 Initial recovery template

Example only; tenant must approve final copy:

> Hi, this is {{BusinessName}}. Sorry we missed your call. What service do you need help with? Reply STOP to stop messages.

### 8.5 Inbound opt-out

Normalize and detect provider-supported opt-out words. Set customer and lead suppression state immediately. Cancel pending SMS jobs. Record audit event.

The implemented STOP family is `STOP`, `STOPALL`, `UNSUBSCRIBE`, `CANCEL`,
`END`, and `QUIT`, matched case-insensitively after trimming. The inbound
message, customer opt-out, lead suppression, pending-action cancellation,
receipt, and redacted dashboard audit activity commit atomically.

### 8.6 Delivery callbacks

Update message state for queued, sent, delivered, undelivered, or failed. Permanent failures are not retried blindly.

The worker persists a queued message before the provider call and re-checks the
tenant, phone route, lead state, customer opt-out, and approved active template
inside a serializable transaction. Transient provider/network failures return
the action to Pending and are retried by Hangfire; provider rejections are
terminal and visible on the Message. Duplicate jobs reuse the tenant-scoped
message idempotency key. An expired Running lease is returned to Pending after
five minutes so a worker restart does not strand work.

## 9. Booking integration

MVP level 1:

- tenant-configured booking URL sent by SMS;
- staff manually marks Booked.

Level 2:

- webhook from Calendly/Cal.com or provider adapter;
- match booking to lead using a signed correlation token or phone/email;
- transition to Booked and cancel follow-ups.

Never place sensitive lead data directly in an unsigned query string.

Milestone 6 implements level 1. `POST /api/v1/leads/{leadId}/booking-link`
requires a DashboardOperator session, CSRF token, and current opaque Lead
version. It accepts no caller-provided URL: the Worker renders only the active
workflow's validated HTTPS `BookingUrl` through an approved active
`BookingLink` template. The tenant/workflow/Lead/stage idempotency key and
persisted Message identity prevent a repeat send. Staff use the existing
transition endpoint to mark `Booked`, which atomically cancels pending
automated actions.

`POST /api/v1/leads/{leadId}/scheduled-actions/{actionId}/cancel` lets a
DashboardOperator cancel a visible Pending action owned by the same tenant and
Lead. Cross-tenant identifiers remain indistinguishable from missing records.

## 10. Email integration

Use for staff notifications, not customer marketing in MVP.

Notification types:

- urgent/needs-human lead;
- automation failure;
- integration disconnected;
- daily operational summary.

## 11. AI provider integration

Application interface:

```csharp
public interface ILeadAnalysisService
{
    Task<LeadAnalysisResult> AnalyzeAsync(
        LeadAnalysisRequest request,
        CancellationToken cancellationToken);
}
```

Input should include only:

- approved service categories;
- tenant service area rules where needed;
- recent relevant customer messages;
- deterministic safety instructions;
- schema version.

Output must conform to the schema in `docs/06_AI_GUARDRAILS.md`.

LR-0701 implements this interface in Application and an optional OpenAI
Responses API adapter in Infrastructure. Provider requests use strict
`text.format` JSON Schema, `store: false`, a bounded output size, and no tools.
The provider receives approved categories, optional redacted service-area
guidance, and at most eight recent redacted conversation turns (1,200
characters each and 6,000 total). Raw TenantId, names, notes, authentication
data, and provider metadata are not explicit input fields; email addresses and
phone-like values are masked. A SHA-256-derived tenant safety identifier is
sent instead of the raw tenant ID.

Every attempt has a configured 1-30 second timeout. Network failures and HTTP
408, 409, 429, and 5xx responses receive at most two bounded exponential-delay
retries. Refusal, non-transient HTTP failure, an invalid provider envelope, or
locally schema-invalid output returns a typed failure with no suggestion.
LR-0701 does not persist or invoke analysis and does not send the suggested
reply; those application flows remain LR-0702 and LR-0703.

## 12. Webhook idempotency algorithm

1. Validate signature.
2. Derive an opaque external event key that distinguishes legitimate provider
   status progressions from duplicate delivery; a provider SID alone may be
   insufficient.
3. Begin transaction.
4. Insert `ExternalEventReceipt` with unique key.
5. If unique conflict, return 200 because event was already accepted.
6. Translate payload into internal command.
7. Apply state changes and write outbox/scheduled action.
8. Commit.
9. Return 200.
10. Process external side effects asynchronously.

For call-status callbacks, `ExternalEventId` is a SHA-256 identity over Call SID
plus normalized status and `PayloadHash` covers deterministically ordered form
fields. One serializable transaction contains receipt insertion, route outcome,
lead update/creation, pending action, and audit. `SendInitialRecoverySms` remains
durable intent until Milestone 4 adds worker execution.

## 13. Rate limiting

Apply separate policies:

- browser login endpoints;
- manual message sends;
- public webhook endpoints, with enough burst tolerance for provider retries;
- platform-admin endpoints.

Rate limiting must not cause silent data loss. Provider retries should receive appropriate status codes.

## 14. OpenAPI

The initial skeleton is in `api/openapi.yaml`. Codex must keep it aligned with implementation or generate it from annotated endpoints and commit a verified export.
