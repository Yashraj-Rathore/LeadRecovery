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

- `POST /api/v1/auth/login`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/refresh` if a refresh design is used
- `GET /api/v1/auth/me`
- `POST /api/v1/auth/forgot-password`
- `POST /api/v1/auth/reset-password`

Prefer secure cookie sessions under a same-origin deployment. Avoid exposing long-lived tokens to browser JavaScript.

## 4. Lead endpoints

### List leads

`GET /api/v1/leads?status=NeedsHuman&urgency=High&assignedTo=me&cursor=...`

Response includes summary fields only.

### Get lead

`GET /api/v1/leads/{leadId}`

Returns:

- lead details;
- current status;
- customer summary;
- assignment;
- automation state;
- conversation timeline;
- pending actions;
- latest AI suggestion;
- audit summary appropriate to user role.

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

### Pause automation

`POST /api/v1/leads/{leadId}/automation/pause`

### Resume automation

`POST /api/v1/leads/{leadId}/automation/resume`

### Add internal note

`POST /api/v1/leads/{leadId}/notes`

## 5. Message endpoints

- `GET /api/v1/leads/{leadId}/messages`
- `POST /api/v1/leads/{leadId}/messages`
- `GET /api/v1/messages/{messageId}/status`

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

### 8.2 Required controls

- Validate Twilio signature against the exact public URL and form values.
- Support proxy/ingress forwarded headers safely so signature validation uses the canonical URL.
- Reject invalid signatures with 403.
- Return 2xx quickly after durable receipt.
- Use provider SID plus event type for idempotency.
- Never trust tenant ID from webhook form fields.
- Resolve tenant through the called/messaged Twilio number.

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

### 8.6 Delivery callbacks

Update message state for queued, sent, delivered, undelivered, or failed. Permanent failures are not retried blindly.

## 9. Booking integration

MVP level 1:

- tenant-configured booking URL sent by SMS;
- staff manually marks Booked.

Level 2:

- webhook from Calendly/Cal.com or provider adapter;
- match booking to lead using a signed correlation token or phone/email;
- transition to Booked and cancel follow-ups.

Never place sensitive lead data directly in an unsigned query string.

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

## 13. Rate limiting

Apply separate policies:

- browser login endpoints;
- manual message sends;
- public webhook endpoints, with enough burst tolerance for provider retries;
- platform-admin endpoints.

Rate limiting must not cause silent data loss. Provider retries should receive appropriate status codes.

## 14. OpenAPI

The initial skeleton is in `api/openapi.yaml`. Codex must keep it aligned with implementation or generate it from annotated endpoints and commit a verified export.
