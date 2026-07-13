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

## 9. Fallback behavior

If AI fails:

- the inbound message is still stored;
- lead activity is updated;
- deterministic qualification continues;
- the lead can be flagged `NeedsHuman`;
- no customer-facing error mentions AI;
- retry only transient failures with bounded attempts;
- avoid duplicate analysis using input hash and schema version.

## 10. Human review

The UI must allow staff to:

- accept suggestion;
- edit category, urgency, summary;
- reject suggestion;
- see that content was AI-generated;
- optionally provide a correction reason.

Corrections are used for product evaluation, not model training unless a separate consented process is created.

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
