namespace LeadRecovery.Application.Leads;

public sealed record LeadInboxPage(
    IReadOnlyList<LeadInboxItem> Items,
    string? NextCursor);
