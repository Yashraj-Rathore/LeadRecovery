namespace LeadRecovery.Contracts.Leads;

public sealed record LeadSummaryResponse(
    Guid Id,
    string? DisplayName,
    string PrimaryPhoneE164,
    string Source,
    string Status,
    string Urgency,
    string AutomationState,
    Guid? AssignedUserId,
    string? AssignedUserName,
    DateTimeOffset LastActivityAtUtc,
    bool HasUnreadCustomerActivity,
    string RowVersion,
    DateTimeOffset CreatedAtUtc);
