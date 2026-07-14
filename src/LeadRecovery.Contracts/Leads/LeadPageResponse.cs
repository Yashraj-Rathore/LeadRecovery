namespace LeadRecovery.Contracts.Leads;

public sealed record LeadPageResponse(
    IReadOnlyList<LeadSummaryResponse> Items,
    string? NextCursor);
