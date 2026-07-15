namespace LeadRecovery.Application.Integrations;

public enum CallStatusProcessingOutcome
{
    Duplicate,
    IgnoredUnknownNumber,
    IgnoredTenantInactive,
    IgnoredStatus,
    IgnoredAutomationDisabled,
    IgnoredCooldown,
    RecoveryScheduled,
}
