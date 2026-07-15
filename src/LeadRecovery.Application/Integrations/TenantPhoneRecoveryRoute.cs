namespace LeadRecovery.Application.Integrations;

public sealed record TenantPhoneRecoveryRoute(
    Guid TenantId,
    bool IsOperational,
    IReadOnlyCollection<string> RecoverableCallStatuses,
    int InitialDelaySeconds,
    int RecoveryCooldownSeconds)
{
    public bool CanRecover(string callStatus) =>
        RecoverableCallStatuses.Contains(callStatus, StringComparer.Ordinal);
}
