namespace LeadRecovery.Contracts.Automations;

public sealed record AutomationStatusResponse(
    bool GlobalEnabled,
    bool TenantEnabled,
    bool EffectiveEnabled,
    string TenantRowVersion,
    int CancelledActionCount);

public sealed record SetTenantAutomationRequest(
    bool Enabled,
    string ExpectedRowVersion,
    string ReasonCode);
