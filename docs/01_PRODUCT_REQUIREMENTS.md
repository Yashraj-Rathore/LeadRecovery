# 01 - Product Requirements

## 1. Product summary

LeadRecovery is a business workflow platform for home-service companies. Its first purpose is to recover callers who would otherwise be lost when staff cannot answer the phone.

The initial release is not a general CRM and not an autonomous AI receptionist. It is a narrow, reliable workflow:

1. A potential customer calls the business.
2. The call is not answered or is marked busy/failed.
3. The system creates or updates a lead.
4. The system sends a pre-approved SMS.
5. The caller replies with job details.
6. The system records the conversation and optionally categorizes it.
7. The caller receives a booking link or callback confirmation.
8. Staff see the lead, take over, assign it, book it, or close it.
9. Follow-ups stop when the lead is booked, closed, or opted out.

## 2. Product goals

### MVP goals

- Demonstrate missed-call recovery end to end.
- Reduce the time from missed call to first response to less than one minute in normal operation.
- Give office staff one view of leads and message history.
- Support manual takeover at any point.
- Prevent duplicate processing from repeated webhooks.
- Operate without AI if the AI provider is unavailable.
- Be deployable locally with Docker Compose.
- Be deployable to Kubernetes for portfolio and scaling demonstration.
- Be sufficiently configurable to pilot with a real Ontario plumbing company.

### Business goals

- Create a two-minute sales demonstration.
- Support a fixed-scope paid pilot.
- Make the core workflow reusable across several businesses.
- Gather evidence for later productization.

## 3. Non-goals for MVP

The MVP must not become:

- a full CRM replacement;
- a field-service dispatch platform;
- an invoicing or payment processor;
- a fully autonomous voice agent;
- a native mobile application;
- a multi-region enterprise platform;
- a custom machine-learning training project;
- a marketplace for contractors;
- an advanced analytics product;
- a generic chatbot builder.

## 4. Primary users

### 4.1 Business owner

Needs to know whether missed leads are being recovered and whether the service creates booked work.

Key actions:

- view lead pipeline;
- review response-time metrics;
- configure business hours, messages, and booking URL;
- review audit history;
- manage staff access.

### 4.2 Office manager or dispatcher

The main daily user.

Key actions:

- view new and urgent leads;
- read call/SMS history;
- assign leads;
- send a manual response;
- stop automation;
- mark booked, won, lost, or spam;
- correct AI-generated categorization or summary.

### 4.3 Technician or limited staff user

May receive assigned-lead notifications and view only relevant information.

### 4.4 Platform administrator

Manages tenant onboarding and support. This role is internal and must not have unrestricted access without explicit support authorization and audit logging.

### 4.5 Caller/customer

Does not create an account. Interacts through phone, SMS, and booking link. Must be able to stop messages.

## 5. Initial customer profile

The first target is an Ontario plumbing company with:

- 3-20 employees;
- an existing business phone number or willingness to use/forward through Twilio;
- regular inbound calls;
- no internal software-development team;
- a website and basic booking or callback process;
- a clear cost when leads receive slow responses.

## 6. Core use cases

### UC-01 Recover a missed call

**Trigger:** Twilio reports a call as no-answer, busy, failed, or another configured recoverable status.

**Success:** One lead is created or updated and one initial recovery message is queued/sent within the configured delay.

**Rules:**

- Duplicate status callbacks must not create duplicate leads or SMS messages.
- A caller who has opted out must not receive an automated SMS.
- A configurable cooldown prevents repeated recovery texts for repeated calls in a short period.
- Staff can disable automation globally or per lead.

### UC-02 Receive and process an SMS reply

**Trigger:** Twilio sends an inbound SMS webhook.

**Success:** The message is persisted, associated with the correct tenant/lead, visible in the dashboard, and the workflow advances.

**Rules:**

- STOP and equivalent opt-out keywords immediately suppress future automated messages.
- Unknown numbers may create a lead only if the tenant configuration permits it.
- Media messages are out of MVP scope unless explicitly enabled later.

### UC-03 Qualify a lead

The system asks a small, pre-approved sequence of questions, for example:

- What service do you need?
- What city or postal area is the property in?
- Is this urgent or actively causing damage?
- Would you like a booking link or a callback?

Question order is deterministic. AI may summarize answers but may not control all branching without rules.

Milestone 6 stores the ordered questions in one active versioned tenant
workflow. Required-text and approved-choice answers are evaluated without AI
and persisted as structured qualification answers. Unknown or multi-match
responses move the Lead to `NeedsHuman`, set `CriticalReview`, cancel pending
automation, and create immediate or business-hours-aligned review audit data
according to tenant policy.

### UC-04 Book or request callback

The system sends a tenant-configured booking URL or records a callback request. Booking confirmation may be manual in MVP unless a calendar integration is configured.

The implemented MVP accepts only an absolute HTTPS booking URL without
embedded credentials. Owner, Manager, or Staff can queue that approved link
for a qualified Lead and manually mark the Lead booked; no calendar dependency
is required.

### UC-05 Staff takeover

A staff user may pause automation, send a manual message, assign the lead, and set the next action.

### UC-06 Follow up

If the customer has not replied or booked, scheduled messages may be sent according to a tenant-approved cadence.

Default pilot cadence:

- initial text: within 30-60 seconds after confirmed missed call;
- follow-up 1: after 2 hours during permitted hours;
- follow-up 2: next business morning;
- then stop unless tenant policy explicitly allows another step.

The implemented tenant workflow allows zero through three uniquely ordered
follow-ups. Every action is moved into the next permitted tenant-timezone
window and re-checks tenant automation, Lead state, opt-out, customer activity,
workflow stage, and approved template at execution.

### UC-07 Close a lead

`Booked` and `ClosedWon` are explicit lead statuses, not close reasons. Booking
stops automated follow-ups, while a later staff-confirmed win may transition a
booked lead to `ClosedWon`.

Reasons for transitioning a lead to `Closed`:

- Lost - no response
- Lost - out of area
- Lost - unavailable service
- Duplicate
- Spam
- Opted out

Closure cancels pending automated follow-ups.

## 7. Functional requirements

### FR-001 Tenant configuration

Each tenant must have:

- business name;
- timezone;
- business hours;
- phone/SMS configuration;
- booking URL;
- approved message templates;
- follow-up cadence;
- allowed service categories;
- permitted service area;
- notification recipients;
- retention settings;
- automation enable/disable control.

### FR-002 Lead management

The system must support:

- lead list with filters;
- lead detail view;
- assignment;
- status transitions;
- urgency;
- category;
- conversation timeline;
- internal notes;
- automation state;
- audit history.

### FR-003 Messaging

- outbound templated SMS;
- inbound SMS processing;
- delivery status callbacks;
- retry policy for temporary failures;
- explicit failure state for permanent errors;
- manual staff message;
- opt-out handling;
- business-hours restrictions.

### FR-004 Authentication and authorization

- tenant users authenticate securely;
- roles: Owner, Manager, Staff, ReadOnly, PlatformAdmin;
- users can access only their tenant unless a support workflow explicitly grants temporary access;
- sensitive administration actions are audited.

### FR-005 Background jobs

- initial recovery job;
- follow-up jobs;
- notification jobs;
- stale-lead checks;
- cleanup/retention jobs;
- retries with bounded backoff;
- cancellation when workflow state changes.

### FR-006 AI assistance

Optional features:

- category suggestion;
- urgency suggestion;
- conversation summary;
- suggested staff response;
- extraction of city/postal area and requested service.

All AI output must be structured, versioned, confidence-scored, and editable by staff.

Milestone 7 implements the optional provider-neutral request/result contract,
version 1.0 strict local validator, OpenAI Responses API adapter, durable
workflow invocation, immutable suggestion persistence, and authorized staff
accept/edit/reject review. Analysis is explicitly enabled, uses the active
workflow's approved category snapshot and bounded redacted recent context, and
never controls deterministic qualification or sends a suggested reply.
Provider/validation failure is recorded once and may route the Lead to
`NeedsHuman`; the already committed deterministic workflow remains available.

### FR-007 Reporting

MVP metrics:

- missed calls detected;
- recovery SMS sent;
- customer reply rate;
- average first-response time;
- leads booked;
- leads closed;
- automation failures;
- opt-out rate.

Metrics must be clearly labeled as operational indicators, not guaranteed revenue attribution.

### FR-008 Audit trail

Audit events include:

- lead status changes;
- assignment changes;
- manual messages;
- automation pause/resume;
- template/configuration changes;
- user/role changes;
- support access;
- AI suggestion accepted or edited.

## 8. Non-functional requirements

### Reliability

- Webhook acknowledgement target: under 2 seconds, with work queued when needed.
- Duplicate external events must be safely ignored.
- Core workflow must continue when AI is down.
- Failed outbound messages must be visible and retryable when appropriate.

### Performance

- Dashboard lead list p95 response target: under 500 ms for pilot-scale data.
- Lead detail p95 response target: under 700 ms.
- Staff dashboard should update within 5 seconds after a new inbound message.

### Availability

Pilot target: 99.5% monthly availability excluding scheduled maintenance. This is a target, not an SLA until contracts define it.

### Security

- HTTPS in non-local environments;
- signature validation for Twilio webhooks;
- least-privilege authorization;
- encrypted transport and managed-database encryption at rest;
- secret-manager integration;
- dependency and container scanning;
- tenant isolation tests.

### Privacy

- collect only information needed for lead handling;
- provide configurable retention;
- support deletion/export requests at tenant level;
- do not use customer message content to train models;
- send only the minimum necessary text to an AI provider;
- maintain data-processing documentation for pilots.

### Accessibility

The dashboard should target WCAG 2.2 AA practices:

- keyboard navigation;
- visible focus;
- semantic labels;
- adequate contrast;
- status not conveyed by color alone;
- accessible validation and alerts.

### Cost control

Pilot infrastructure must be supportable at low volume. Every paid external call should be observable by tenant and type.

## 9. Success criteria for the portfolio demo

The demo is complete when it can show:

1. A test call is missed.
2. A lead is created once.
3. An SMS arrives automatically.
4. The caller replies.
5. The reply appears in the dashboard.
6. The lead receives category/urgency suggestions.
7. Staff marks the lead Booked.
8. Scheduled follow-ups are cancelled.
9. A duplicate webhook does not duplicate data.
10. STOP prevents further automated messages.

## 10. Success criteria for the first pilot

- A real tenant is configured without code changes.
- At least one staff user can operate the dashboard.
- Webhook failures and message failures generate an alert.
- A documented rollback or disable switch exists.
- The tenant approves all outbound message templates.
- The pilot has defined measurements and a support contact.
