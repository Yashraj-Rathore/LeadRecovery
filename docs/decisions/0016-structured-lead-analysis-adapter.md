# ADR-0016: Structured lead-analysis adapter

- Status: Accepted
- Date: 2026-07-21
- Decision owners: LeadRecovery maintainers

## Context

LR-0701 requires an optional AI provider abstraction that can suggest a service
category, urgency, summary, extracted fields, and a staff-review reply without
controlling the deterministic workflow. Provider output is untrusted customer
data processing: only minimum recent context may leave the platform, invalid
output must not become a suggestion, retries must be bounded, and the platform
must not expose an API key or message content in logs.

The OpenAI Responses API supports strict JSON Schema output through
`text.format`. The API can also disable response storage with `store: false`.
The current configured default model is `gpt-5.6-sol`, which supports the
Responses API and structured outputs. The model remains operator-overridable so
representative evaluations can choose a more suitable cost/latency tier later.

## Decision

1. Application owns provider-neutral `ILeadAnalysisService`, request, result,
   suggestion, failure, and validator contracts. Application and Domain do not
   reference HTTP or OpenAI packages.
2. Schema version `1.0` exactly represents the documented suggestion fields.
   Local validation rejects malformed JSON, missing or additional properties,
   unapproved categories, invalid urgency/confidence values, duplicate or
   malformed reason codes, and over-limit text. A refusal or invalid response
   returns a typed failure with no suggestion.
3. Medium/low-confidence output and known safety-sensitive reason codes force
   `RequiresHumanReview` even if the provider returns `false`. This is a
   conservative platform policy, not trust in model confidence.
4. Infrastructure calls `POST https://api.openai.com/v1/responses` through a
   typed `HttpClient` using centrally pinned `Microsoft.Extensions.Http`
   10.0.9. No provider SDK is added because LR-0701 needs one small stable HTTP
   contract and keeping provider-specific translation in one adapter preserves
   the application boundary.
5. Every request uses strict `json_schema` output, `store: false`, a bounded
   output-token limit, and a SHA-256-derived tenant safety identifier rather
   than the raw tenant ID. The adapter sends only the schema version, approved
   categories, optional service-area guidance, and up to eight recent turns.
   Each turn and the total transcript are capped; phone-like values and email
   addresses are masked. Names, notes, authentication data, provider metadata,
   and the raw TenantId are not explicit request fields.
6. Each provider attempt has a 1-30 second configured timeout. Only network
   failures and HTTP 408, 409, 429, and 5xx responses are retried, with zero to
   two exponential-delay retries. A provider response is capped at 64 KiB.
   HTTP rejection, refusal, invalid envelopes, and schema-invalid output are not
   retried.
7. Logs contain provider, model reference, attempt count, and a bounded outcome
   code only. They exclude request/response bodies, contact details, and keys.
8. AI remains disabled by default. LR-0701 registers the adapter in the Worker
   only when explicitly enabled. Analysis persistence, workflow invocation,
   staff accept/edit/reject controls, and outage routing remain LR-0702 and
   LR-0703; no customer-facing message is sent from this adapter.

## Consequences

- Strict provider-side generation and independent local validation create two
  enforcement layers before a suggestion can be trusted.
- Configuration errors fail closed when AI is explicitly enabled, while the
  existing deterministic worker continues unchanged when it is disabled.
- Email/phone masking is deterministic and testable, but complete natural-name
  removal cannot be inferred safely from arbitrary prose. Callers must not add
  names or unrelated history to the provider-neutral request.
- Model quality, cost, and safety thresholds still require the fictional
  evaluation set before production use. LR-0701 does not claim Milestone 7 is
  complete.

## References

- [OpenAI structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [OpenAI Responses API create reference](https://developers.openai.com/api/reference/resources/responses/methods/create)
- [OpenAI GPT-5.6 Sol model](https://developers.openai.com/api/docs/models/gpt-5.6-sol)
