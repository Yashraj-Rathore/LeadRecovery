# 03 - Domain Model and Database

## 1. Domain principles

- Every tenant-owned record carries `TenantId`.
- External identifiers are stored with provider name and are unique within the correct scope.
- All timestamps are stored in UTC.
- Display and scheduling use the tenant timezone.
- Customer phone numbers are validated and normalized to canonical E.164 before
  persistence. Invalid or unknown numbers are rejected explicitly.
- State changes are explicit and auditable.
- Soft deletion is used only where business or retention rules require it; otherwise archive/close states are preferred.

## 2. Core entities

### Tenant

Represents one business customer.

Key fields:

- `Id`
- `Name`
- `Slug`
- `TimezoneId`
- `Status` - Trial, Active, Suspended, Closed
- `AutomationEnabled`
- `DataRetentionEnabled` opt-in, false by default
- `DataRetentionDays` from 30 through 3,650, default 365
- `Version` application-managed `bigint` concurrency token
- `CreatedAtUtc`
- `UpdatedAtUtc`

### TenantPhoneNumber

Maps a Twilio number or verified business number to a tenant.

- `Id`
- `TenantId`
- `Provider`
- `PhoneNumberE164`
- `ProviderNumberSid`
- `InboundSmsEnabled`
- `MissedCallRecoveryEnabled`
- `IsPrimary`
- `RecoverableCallStatuses` non-empty normalized provider status set
- `InitialDelaySeconds` from 0 through 3,600
- `RecoveryCooldownSeconds` from 1 through 86,400

Unique: `(Provider, ProviderNumberSid)`, `(Provider, PhoneNumberE164)`, and
`(TenantId, PhoneNumberE164)`. Global provider/phone uniqueness guarantees that
one destination cannot route to multiple tenants. In Milestone 3 this entity is
the narrow tenant-specific recovery-policy boundary; a later settings milestone
may move timing and status configuration into a versioned workflow definition.

### User

Implemented with ASP.NET Core Identity using a `Guid` primary key.

- `Id`
- `DisplayName`
- normalized username and email fields managed by Identity
- password hash, security stamp, lockout, and other Identity security fields
- `IsActive`
- `CreatedAtUtc`

### TenantMembership

- `Id`
- `TenantId`
- `UserId`
- `Role` - Owner, Manager, Staff, ReadOnly
- `CreatedAtUtc`

Unique: `(TenantId, UserId)`. A membership row is the grant; removing it
revokes that tenant grant. User-wide disablement uses `User.IsActive`, while
tenant-wide suspension uses `Tenant.Status`. The cookie validator checks all
three on every request. Milestone 2 supports exactly one Trial/Active membership
per login and fails closed when a user has zero or multiple active memberships;
tenant switching requires a later explicit design.

### Lead

- `Id`
- `TenantId`
- `CustomerId` nullable
- `PrimaryPhoneE164`
- `DisplayName` nullable
- `Source` - MissedCall, InboundSms, WebForm, Manual, Import
- `Status`
- `Urgency`
- `ServiceCategoryId` nullable
- `AssignedUserId` nullable
- `AutomationState` - Active, Paused, Completed, Suppressed
- `LastCustomerActivityAtUtc`
- `LastBusinessActivityAtUtc`
- `BookedAtUtc` nullable
- `ClosedAtUtc` nullable
- `CloseReason` nullable
- `Version` application-managed `bigint` concurrency token, exposed through APIs
  as an opaque base64 value
- audit timestamps

LR-0201 implements this aggregate and its lifecycle policy in the domain layer.
LR-0203 adds Lead persistence as the required tenant-owned parent for
Conversation and Message. Lead uses the same server-derived tenant read/write
guards as those child records and an application-managed concurrency version.
When `AssignedUserId` is present, `(TenantId, AssignedUserId)` must reference a
membership in the same tenant.

Milestone 5 adds explicit aggregate methods for same-tenant assignment,
unassignment, urgency changes, user pause, and user resume. Assignment target
validity is checked in persistence against the active tenant membership;
pause/resume state and terminal-Lead restrictions remain domain rules.

Indexes:

- `(TenantId, Status, CreatedAtUtc desc)`
- `(TenantId, PrimaryPhoneE164, CreatedAtUtc desc)`
- `(TenantId, AssignedUserId, Status)`
- `(TenantId, Urgency, Status)`

### Customer

Optional normalized contact record.

- `Id`
- `TenantId`
- `PhoneE164` required, maximum 16 characters
- `Name` nullable, maximum 200 characters
- `Email` nullable, maximum 320 characters
- `City` nullable, maximum 100 characters
- `PostalCode` nullable, maximum 20 characters
- `SmsConsentBasis` nullable, maximum 100 characters
- `OptedOutAtUtc` nullable
- `CreatedAtUtc`

Unique: `(TenantId, PhoneE164)`.

LR-0202 implements Customer persistence and a creation use case that derives
`TenantId` from the active server context. The application depends on a phone
normalization interface; Infrastructure implements it with
`libphonenumber-csharp` and stores only canonical E.164 values. Customer reads
use an EF tenant query filter, and writes reject missing or mismatched tenant
context. LR-0203 and LR-0204 apply equivalent persistence controls to the other
tenant-owned Milestone 1 entities. LR-0102 still owns endpoint-level proof that
browser input cannot override server-derived TenantId when feature APIs arrive.

### CallEvent

- `Id`
- `TenantId`
- `LeadId` nullable until linked
- `Provider`
- `ProviderCallSid`
- `FromPhoneE164`
- `ToPhoneE164`
- `Status`
- `Direction`
- `StartedAtUtc`
- `EndedAtUtc` nullable
- `DurationSeconds` nullable
- `RawPayloadHash`
- `ReceivedAtUtc`

Unique: `(Provider, ProviderCallSid, Status, ReceivedAtUtc bucket)` or a provider-event key. The exact idempotency strategy must account for Twilio sending multiple legitimate status updates for one call.

### Conversation

- `Id`
- `TenantId`
- `LeadId`
- `Channel` - Sms
- `Status` - Open, Closed
- `CreatedAtUtc`
- `ClosedAtUtc` nullable

Conversations start `Open`, may transition once to `Closed`, and cannot reopen
without a future explicit audited use case.

### Message

- `Id`
- `TenantId`
- `LeadId`
- `ConversationId`
- `Direction` - Inbound, Outbound
- `Kind` - Automated, Manual, System
- `Provider` maximum 50 characters
- `ProviderMessageSid` nullable until sent, maximum 100 characters
- `ClientIdempotencyKey` required, maximum 200 characters
- `Body` required, maximum 1,600 characters
- `Status` - Queued, Sent, Delivered, Failed, Received, Suppressed
- `FailureCode` nullable, maximum 100 characters
- `FailureDescription` nullable, maximum 500 characters
- `SentByUserId` nullable
- `TemplateId` nullable
- `CreatedAtUtc`
- `SentAtUtc` nullable
- `DeliveredAtUtc` nullable

Unique: `(Provider, ProviderMessageSid)` when not null; `(TenantId, ClientIdempotencyKey)`.

Inbound messages begin in terminal `Received`. Outbound messages begin
`Queued`; allowed transitions are `Queued -> Sent -> Delivered`,
`Queued/Sent -> Failed`, and `Queued -> Suppressed`. `Delivered`, `Failed`, and
`Suppressed` are terminal. A client idempotency key is required for every
message; inbound adapters derive an opaque server-controlled key rather than
trusting tenant or provider input as authority. Message bodies preserve their
content but reject empty input and content longer than the provider-supported
1,600-character ceiling.

LR-0203 persists inbound and outbound records without calling a provider. Lead,
Conversation, and Message reads are tenant-filtered; their writes reject missing
or mismatched tenant context; and compound foreign keys prevent cross-tenant
relationships. Provider calls and idempotent callback handlers remain in later
Twilio and worker issues.

### MessageTemplate

- `Id`
- `TenantId`
- `Name`
- `Purpose`
- `Body`
- `Version`
- `IsApproved`
- `IsActive`
- `CreatedByUserId`
- `ApprovedByUserId` nullable
- `CreatedAtUtc`
- `ApprovedAtUtc` nullable

Templates are immutable after approval; edits create a new version.

LR-0402 persists this aggregate with tenant read/write guards, a compound
tenant identity used by Message, and a filtered unique index that permits only
one active template per `(TenantId, Purpose)`. Activation is rejected until the
template is approved. Initial recovery execution requires the active approved
`InitialMissedCallRecovery` purpose and stores its ID on the outbound Message.

### LeadNote

- `Id`
- `TenantId`
- `LeadId`
- `AuthorUserId`
- `Body` required, maximum 2,000 characters
- `CreatedAtUtc`

Milestone 5 persists internal notes as plain text. Compound foreign keys bind
the note to a Lead and author membership in the same tenant. Reads and writes
use the tenant query/write guards, and `(TenantId, LeadId, CreatedAtUtc)`
supports ordered timeline projection. Notes never execute as HTML.

### WorkflowDefinition

MVP can use configuration rather than a general visual workflow engine.

- `Id`
- `TenantId`
- `Name`
- `Version`
- `IsActive`
- `BookingUrl` - absolute HTTPS without embedded credentials
- `FollowUpPolicyJson`
- `BusinessHoursPolicyJson`
- `QualificationPolicyJson`
- audit timestamps

Milestone 6 persists one active workflow per tenant through a filtered unique
index and retains unique `(TenantId, Version)` history. Construction validates
one through ten unique ordered questions, at least one business-hours window,
one window per day, and at most three follow-ups with unique sequence numbers
and template purposes. JSON is a persistence format for validated policy, not
an untrusted dynamic execution language.

### QualificationAnswer

- `Id`
- `TenantId`
- `LeadId`
- `SourceMessageId`
- `QuestionKey`
- `Value` nullable when unresolved
- `Outcome` - Accepted, Unknown, Ambiguous
- `CreatedAtUtc`

Unique constraints on `(TenantId, LeadId, QuestionKey)` and
`(TenantId, SourceMessageId)` prevent duplicate structured capture. Compound
foreign keys bind the answer, Lead, and source Message to the same tenant.

### ScheduledAction

- `Id`
- `TenantId`
- `LeadId`
- `ActionType` maximum 100 characters
- `ScheduledForUtc`
- `Status` - Pending, Running, Completed, Cancelled, Failed
- `AttemptCount`
- `IdempotencyKey` maximum 200 characters
- `PayloadJson` required JSON object, maximum 16,384 characters
- `LastError` nullable, maximum 1,000 characters
- audit timestamps

Unique: `(TenantId, IdempotencyKey)`.

Actions start `Pending`. Allowed transitions are `Pending -> Running`,
`Pending -> Cancelled`, `Running -> Completed`, `Running -> Failed`, and
`Running -> Pending` for a retry with a new due time at or after the retry
decision. A Pending action may also be deferred to a future permitted window
without consuming an attempt. Starting an attempt increments `AttemptCount`.
Completed, Failed, and Cancelled are terminal. The due-work index is `(Status, ScheduledForUtc)`; a
separate `(TenantId, LeadId, Status)` index supports deterministic cancellation.

Milestone 6 uses action types `SendQualificationQuestion`, `SendBookingLink`,
and `SendFollowUpSms`. Idempotency keys include Lead, workflow version, stage,
and sequence as applicable. The booking transition cancels that Lead's pending
automated actions; running and terminal actions are not rewritten.

LR-0703 adds `AnalyzeLead`. Its JSON payload snapshots the source inbound
Message, analysis schema, active workflow identity/version, category-question
key, and allowed categories. A newer inbound reply cancels older Pending
analysis actions for the Lead. The Worker permits one provider invocation per
action at job level; Completed, Failed, and Cancelled remain terminal.

### ExternalEventReceipt

The integration/system idempotency ledger. It is not ordinary tenant-owned
business data because a receipt may need to be recorded before a tenant can be
resolved. When an event maps to a tenant, `TenantId` is recorded and remains
immutable. This entity is never exposed through tenant browser APIs.

- `Id`
- `TenantId` nullable
- `Provider` maximum 50 characters
- `EventType` maximum 100 characters
- `ExternalEventId` maximum 200 characters
- `PayloadHash` maximum 128 characters
- `ReceivedAtUtc`
- `ProcessedAtUtc` nullable
- `ProcessingResult` nullable, maximum 500 characters

Unique: `(Provider, EventType, ExternalEventId)`.

`ExternalEventId` is an opaque adapter-generated value. Provider adapters must
distinguish legitimate status progressions from duplicate delivery; a provider
SID by itself is not necessarily sufficient.

LR-0204 permits unresolved receipts to be saved without a request tenant
context. A non-empty TenantId may be assigned once after resolution and cannot
then be cleared or changed. Processing may be recorded once at or after receipt.
The ledger has no tenant query filter and is not exposed through browser APIs;
later integration handlers must authorize its system-level access explicitly.

### AiAnalysis

- `Id`
- `TenantId`
- `LeadId`
- `SchemaVersion`
- `Provider`
- `ModelReference`
- `InputHash`
- `AllowedCategoriesJson`
- `CategorySuggestion`
- `UrgencySuggestion`
- `Summary`
- optional extracted city, postal code, and callback window
- optional `SuggestedReply`
- `Confidence`
- `RequiresHumanReview`
- `ReasonCodesJson`
- `RawStructuredOutputJson`
- `ReviewStatus` - Pending, Accepted, Edited, Rejected
- separate reviewed category, urgency, summary, extracted fields, and suggested
  reply, nullable until accepted or edited
- `CorrectionReason` nullable
- `ReviewedByUserId` nullable
- `ReviewedAtUtc` nullable
- `Version` application-managed `bigint` concurrency token
- `CreatedAtUtc`

Do not store hidden chain-of-thought or unnecessary provider metadata.

LR-0702 persists this tenant-owned record. Original suggestion fields and the
validated structured JSON are immutable; a one-way
`Pending -> Accepted|Edited|Rejected` review stores staff values separately.
`(TenantId, LeadId, SchemaVersion, InputHash)` prevents duplicate analysis of
the same input and schema, while compound Lead and reviewer-membership foreign
keys enforce tenant ownership. The dashboard exposes `Version` as an opaque
review token. LR-0703 computes the hash from the canonical bounded request,
persists the record only after successful validation, and records failure on
the associated action instead of creating an invalid analysis. No schema
migration is required beyond the LR-0702 `AiAnalysis` storage.

### AuditEvent

- `Id`
- `TenantId` nullable for platform-level events
- `ActorType` - User, System, Integration, Support
- `ActorId`
- `Action`
- `EntityType`
- `EntityId`
- `BeforeJson` nullable, redacted
- `AfterJson` nullable, redacted
- `CorrelationId`
- `CreatedAtUtc`

Milestone 2 persists this append-oriented foundation and records successful
login and logout events with correlation IDs. It is not exposed through tenant
browser APIs. Redacted before/after JSON is available for later audited domain
changes; secrets and session material are prohibited.

Milestone 3 records redacted call-status outcomes and scheduled-recovery
decisions. It never stores the Twilio auth token, request signature, raw form
payload, or phone number in audit JSON.

### Notification

- `Id`
- `TenantId`
- `UserId` nullable
- `LeadId` nullable
- `Channel`
- `Type`
- `Status`
- `DestinationMasked`
- `CreatedAtUtc`
- `SentAtUtc` nullable

## 3. Enums

### LeadStatus

```text
New
Contacting
AwaitingCustomer
Qualified
BookingOffered
NeedsHuman
Booked
Closed
ClosedWon
```

### LeadUrgency

```text
Unknown
Low
Normal
High
CriticalReview
```

`CriticalReview` means urgent human attention. It does not authorize the system to provide technical emergency instructions.

### AutomationState

```text
Active
PausedByUser
PausedBySystem
Completed
SuppressedOptOut
SuppressedPolicy
```

## 4. State-transition rules

Examples:

- `New -> Contacting` only when a recovery action is queued or sent.
- `AwaitingCustomer -> Qualified` only when minimum required fields are present or staff overrides with a reason.
- Any pre-booking active state may move to `NeedsHuman`.
- Any pre-booking active state, including `NeedsHuman`, may move to `Closed`
  with a documented close reason.
- `Booked` cancels pending follow-ups and sets automation to completed.
- `Booked -> ClosedWon` records a later staff-confirmed win.
- `Closed` requires one of the documented loss, duplicate, spam, or opt-out
  reasons. `Booked` and `Won` are statuses and are not close reasons.
- `SuppressedOptOut` prevents all non-essential automated SMS.
- `Closed` and `ClosedWon` are terminal for LR-0201. Reopening is deferred until
  an application use case can require and persist an audit event.
- Message delivery states follow the LR-0203 policy: only queued outbound
  messages can be sent or suppressed; sent messages can be delivered; queued or
  sent messages can fail; final and inbound-received states cannot transition.
- Scheduled actions follow the LR-0204 transition graph; only Pending actions
  can start or cancel, only Running actions can retry, complete, or fail, and
  terminal states cannot transition.

## 5. Tenant isolation

Required implementation controls:

1. Resolve tenant context from authenticated membership, not request body.
2. Resolve webhook tenant from the destination provider number mapping.
3. Apply global query filters to tenant-owned EF entities.
4. For administrative background jobs, pass and validate TenantId explicitly.
5. Use compound `(TenantId, Id)` keys and foreign keys for relationships between
   tenant-owned entities so the database also rejects cross-tenant links.
6. Run integration tests that attempt cross-tenant access for every sensitive endpoint family.

The implemented browser lead queries derive TenantId from the validated session
membership, apply the EF tenant filter, ignore client tenant headers, and map a
cross-tenant lead identifier to not-found without revealing that it exists.

## 6. Concurrency

- Use application-managed `bigint Version` concurrency tokens on `Lead` and
  `Tenant`. Increment the token whenever the corresponding aggregate is
  updated.
- Return HTTP 409 when a staff update conflicts.
- Make webhook handlers and job handlers idempotent.
- Use database transactions for state transition plus scheduled-action cancellation.

## 7. Retention

Suggested pilot defaults, configurable by contract:

- operational lead/message data: 12 months;
- audit data: 24 months;
- failed webhook payload metadata: 90 days;
- raw payload body: avoid storing unless needed for troubleshooting, and then redact and expire quickly;
- application logs: 30-90 days depending on environment.

Retention must be implemented through scheduled jobs with dry-run reporting before deletion.

LR-0803 applies the operational Lead/message default through an opt-in tenant
policy. The Worker defaults to disabled `dry-run`; enabled runs select only
`Closed`/`ClosedWon` Leads whose `ClosedAtUtc` precedes the tenant cutoff, in
batches of at most 1,000. A batch deletes the selected Leads and their
conversations, messages, notes, qualification answers, scheduled actions, and
AI analyses transactionally with a PII-free count manifest. Customer consent/
opt-out state, AuditEvents, and ExternalEventReceipts remain because they have
separate safety, compliance, and idempotency purposes. Their future expiry
requires a separate accepted policy.

Every batch begins a trusted scope for exactly the policy TenantId and retains
both EF query filters and explicit TenantId predicates. Policy changes or a
scope mismatch fail before mutation. `delete` mode additionally requires an
operator backup acknowledgement; deleted content can be recovered only from a
database backup or point-in-time restore.

## 8. Migration strategy

- EF Core migrations are committed to source control.
- Production migrations run as an explicit deployment job, not automatically from every API replica.
- Every destructive migration requires backup and rollback planning.
- Backward-compatible expand/migrate/contract patterns are preferred after real clients exist.
