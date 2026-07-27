using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Domain.Analysis;

public sealed record AiAnalysisValues(
    string ServiceCategory,
    LeadUrgency Urgency,
    string Summary,
    string? City,
    string? PostalCode,
    string? PreferredCallbackWindow,
    string? SuggestedReply);
