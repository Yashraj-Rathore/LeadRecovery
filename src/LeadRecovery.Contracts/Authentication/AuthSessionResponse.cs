namespace LeadRecovery.Contracts.Authentication;

public sealed record AuthSessionResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    Guid TenantId,
    string TenantName,
    string Role);
