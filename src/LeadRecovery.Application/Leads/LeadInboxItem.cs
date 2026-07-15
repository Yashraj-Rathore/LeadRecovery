using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Leads;

public sealed record LeadInboxItem(
    Guid Id,
    string? DisplayName,
    string PrimaryPhoneE164,
    LeadSource Source,
    LeadStatus Status,
    LeadUrgency Urgency,
    AutomationState AutomationState,
    Guid? AssignedUserId,
    string? AssignedUserName,
    DateTimeOffset LastActivityAtUtc,
    bool HasUnreadCustomerActivity,
    long Version,
    DateTimeOffset CreatedAtUtc);
