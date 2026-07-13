# 03 - Domain Model and Database

## 1. Domain principles

- Every tenant-owned record carries `TenantId`.
- External identifiers are stored with provider name and are unique within the correct scope.
- All timestamps are stored in UTC.
- Display and scheduling use the tenant timezone.
- Phone numbers are normalized to E.164 where possible.
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

Unique: `(Provider, ProviderNumberSid)` and `(TenantId, PhoneNumberE164)`.

### User

Use ASP.NET Core Identity or an equivalent secure implementation.

Tenant membership should be explicit through `TenantUser` if platform users may belong to more than one tenant.

### TenantUser

- `TenantId`
- `UserId`
- `Role`
- `Status`
- `CreatedAtUtc`

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

Indexes:

- `(TenantId, Status, CreatedAtUtc desc)`
- `(TenantId, PrimaryPhoneE164, CreatedAtUtc desc)`
- `(TenantId, AssignedUserId, Status)`
- `(TenantId, Urgency, Status)`

### Customer

Optional normalized contact record.

- `Id`
- `TenantId`
- `PhoneE164`
- `Name`
- `Email`
- `City`
- `PostalCode`
- `SmsConsentBasis`
- `OptedOutAtUtc`
- `CreatedAtUtc`

Unique: `(TenantId, PhoneE164)`.

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
- `ClosedAtUtc`

### Message

- `Id`
- `TenantId`
- `LeadId`
- `ConversationId`
- `Direction` - Inbound, Outbound
- `Kind` - Automated, Manual, System
- `Provider`
- `ProviderMessageSid` nullable until sent
- `ClientIdempotencyKey`
- `Body`
- `Status` - Queued, Sent, Delivered, Failed, Received, Suppressed
- `FailureCode` nullable
- `FailureDescription` nullable
- `SentByUserId` nullable
- `TemplateId` nullable
- `CreatedAtUtc`
- `SentAtUtc` nullable
- `DeliveredAtUtc` nullable

Unique: `(Provider, ProviderMessageSid)` when not null; `(TenantId, ClientIdempotencyKey)`.

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

Templates are immutable after approval; edits create a new version.

### WorkflowDefinition

MVP can use configuration rather than a general visual workflow engine.

- `Id`
- `TenantId`
- `Name`
- `Version`
- `IsActive`
- `InitialDelaySeconds`
- `FollowUpPolicyJson`
- `BusinessHoursPolicyJson`
- `QualificationPolicyJson`

### ScheduledAction

- `Id`
- `TenantId`
- `LeadId`
- `ActionType`
- `ScheduledForUtc`
- `Status` - Pending, Running, Completed, Cancelled, Failed
- `AttemptCount`
- `IdempotencyKey`
- `PayloadJson`
- `LastError`
- audit timestamps

Unique: `(TenantId, IdempotencyKey)`.

### ExternalEventReceipt

The integration/system idempotency ledger. It is not ordinary tenant-owned
business data because a receipt may need to be recorded before a tenant can be
resolved. When an event maps to a tenant, `TenantId` is recorded and remains
immutable. This entity is never exposed through tenant browser APIs.

- `Id`
- `TenantId` nullable
- `Provider`
- `EventType`
- `ExternalEventId`
- `PayloadHash`
- `ReceivedAtUtc`
- `ProcessedAtUtc` nullable
- `ProcessingResult`

Unique: `(Provider, EventType, ExternalEventId)`.

`ExternalEventId` is an opaque adapter-generated value. Provider adapters must
distinguish legitimate status progressions from duplicate delivery; a provider
SID by itself is not necessarily sufficient.

### AiAnalysis

- `Id`
- `TenantId`
- `LeadId`
- `SchemaVersion`
- `Provider`
- `ModelReference`
- `InputHash`
- `CategorySuggestion`
- `UrgencySuggestion`
- `Summary`
- `Confidence`
- `RequiresHumanReview`
- `ReasonCodesJson`
- `RawStructuredOutputJson`
- `AcceptedByUserId` nullable
- `AcceptedAtUtc` nullable
- `CreatedAtUtc`

Do not store hidden chain-of-thought or unnecessary provider metadata.

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
- Any active state may move to `NeedsHuman`.
- `Booked` cancels pending follow-ups and sets automation to completed.
- `Booked -> ClosedWon` records a later staff-confirmed win.
- `Closed` requires one of the documented loss, duplicate, spam, or opt-out
  reasons. `Booked` and `Won` are statuses and are not close reasons.
- `SuppressedOptOut` prevents all non-essential automated SMS.
- Reopening a lead requires an audit event.

## 5. Tenant isolation

Required implementation controls:

1. Resolve tenant context from authenticated membership, not request body.
2. Resolve webhook tenant from the destination provider number mapping.
3. Apply global query filters to tenant-owned EF entities.
4. For administrative background jobs, pass and validate TenantId explicitly.
5. Use compound `(TenantId, Id)` keys and foreign keys for relationships between
   tenant-owned entities so the database also rejects cross-tenant links.
6. Run integration tests that attempt cross-tenant access for every sensitive endpoint family.

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

## 8. Migration strategy

- EF Core migrations are committed to source control.
- Production migrations run as an explicit deployment job, not automatically from every API replica.
- Every destructive migration requires backup and rollback planning.
- Backward-compatible expand/migrate/contract patterns are preferred after real clients exist.
