namespace LeadRecovery.Contracts.Leads;

public sealed record LeadSummaryResponse(
    Guid Id,
    string? DisplayName,
    string PrimaryPhoneE164,
    string Source,
    string Status,
    string Urgency,
    string AutomationState,
    DateTimeOffset CreatedAtUtc);
