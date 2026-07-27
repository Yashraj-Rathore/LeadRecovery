# 06 - AI Design and Guardrails

## 1. Principle

AI improves staff efficiency but does not own the workflow. The system must remain useful and safe when AI is disabled, delayed, unavailable, or wrong.

## 2. Allowed AI functions

- classify a requested service into the tenant's approved categories;
- suggest urgency based on explicit tenant-approved criteria;
- summarize conversation content for staff;
- extract structured fields such as city, postal prefix, and preferred callback time;
- draft a staff response for human review;
- flag ambiguity or possible safety-sensitive language.

## 3. Prohibited autonomous actions

AI must not independently:

- quote or promise prices;
- diagnose plumbing, electrical, HVAC, or other technical problems;
- instruct a caller to perform dangerous repairs;
- guarantee arrival or completion times;
- accept contractual terms;
- reject a lead solely from model output;
- send unrestricted free-form messages;
- change tenant configuration;
- delete data;
- close a lead as won/lost without rules or staff action.

## 4. Structured output schema

```json
{
  "schemaVersion": "1.0",
  "serviceCategory": "LeakRepair",
  "urgency": "High",
  "summary": "Customer reports an active leak in the basement and requests a callback.",
  "extracted": {
    "city": "Mississauga",
    "postalCode": null,
    "preferredCallbackWindow": "As soon as possible"
  },
  "confidence": 0.87,
  "requiresHumanReview": true,
  "reasonCodes": ["ACTIVE_PROPERTY_DAMAGE", "TIME_SENSITIVE"],
  "suggestedReply": "Thanks for the details. A team member will review this and contact you shortly."
}
```

The API must validate this schema. Invalid output is discarded and logged as a provider failure, not passed through.

LR-0701 validates the exact property set again after provider-side strict
schema generation. It rejects missing, duplicate, or additional properties;
unapproved categories; undefined urgency values; confidence outside 0-1;
invalid or duplicate reason codes; blank or over-limit strings; refusals; and
malformed provider envelopes. Medium/low confidence and known safety-sensitive
reason codes force `requiresHumanReview=true` even if the provider returned
false. A failure result never carries a suggestion.

## 5. Confidence policy

Suggested baseline:

- `>= 0.85` and no safety reason: display suggestion normally;
- `0.65-0.84`: display with review badge;
- `< 0.65`: do not automatically apply category/urgency;
- any safety-sensitive reason code: require human review regardless of confidence.

Model confidence is not statistically guaranteed. Treat it as an operational hint and monitor correction rates.

## 6. Prompt design

System prompt requirements:

- state that the model is an internal classification assistant;
- list allowed categories;
- define urgency labels;
- forbid diagnosis and promises;
- require structured output only;
- instruct model to choose `Unknown` when evidence is insufficient;
- require `requiresHumanReview=true` for ambiguous or safety-sensitive content;
- prohibit adding facts not present in messages.

## 7. Data minimization

Before sending to AI:

- remove unrelated historical messages;
- avoid sending full names where not needed;
- mask phone numbers and email addresses;
- omit internal notes unless specifically required;
- omit authentication, payment, and secret data;
- include only the minimum recent conversation context.

## 8. Model/provider abstraction

The application stores a logical capability configuration, not provider-specific business logic.

```csharp
public sealed record LeadAnalysisRequest(
    Guid TenantId,
    IReadOnlyList<string> AllowedCategories,
    IReadOnlyList<ConversationTurn> Turns,
    string SchemaVersion);
```

Provider-specific adapters translate the request and response.

The LR-0701 OpenAI adapter uses the Responses API with strict `json_schema`
output and `store: false`. Its default model is `gpt-5.6-sol`, configurable by
operators for later evaluation. Application and Domain contain no OpenAI or
HTTP references.

## 9. Fallback behavior

If AI fails:

- the inbound message is still stored;
- lead activity is updated;
- deterministic qualification continues;
- the lead can be flagged `NeedsHuman`;
- no customer-facing error mentions AI;
- retry only transient failures with bounded attempts;
- avoid duplicate analysis using input hash and schema version.

LR-0701 bounds each attempt to 1-30 seconds, retries only network/408/409/429/
5xx failures, permits at most two retries, and caps a provider response at 64
KiB. LR-0703 schedules analysis only after deterministic inbound processing has
been persisted. It coalesces older Pending work, disables Hangfire retries for
the analysis job, and suppresses a second provider call after lease recovery.
The canonical request hash and schema version deduplicate successful output.
A typed provider/validation failure terminally fails the action, records only a
bounded redacted code, and routes an eligible Lead to `NeedsHuman`; it does not
create a customer Message. The deterministic qualification result remains
committed and usable whether analysis succeeds, fails, or is disabled.

## 10. Human review

The UI must allow staff to:

- accept suggestion;
- edit category, urgency, summary;
- reject suggestion;
- see that content was AI-generated;
- optionally provide a correction reason.

Corrections are used for product evaluation, not model training unless a separate consented process is created.

LR-0702 implements this review as a one-way staff decision while retaining the
immutable original output. Low confidence below `0.65` is prominently labeled
and never applied automatically. Audits record the decision and corrected field
names without copying summaries, extracted customer data, draft replies, or
correction text. Review routes create no customer-facing action.

## 11. Evaluation set

Create a fictional test set with at least 100 messages covering:

- routine leak;
- clogged drain;
- no hot water;
- out-of-area request;
- unclear request;
- price-only question;
- spam;
- opt-out;
- urgent property damage language;
- messages in informal English;
- typographical errors;
- multiple issues in one message.

Measure:

- category agreement with human label;
- urgency agreement;
- false safe/unsafe rates;
- unsupported facts in summary;
- JSON schema failure rate;
- latency and cost.

No AI feature is production-ready until the evaluation and fallback tests pass agreed thresholds.

## 12. Customer-facing generation

For MVP, customer-facing automated messages should come from approved templates with bounded substitutions. AI-generated customer replies may be introduced later only with human approval or a strict retrieval/template framework.
