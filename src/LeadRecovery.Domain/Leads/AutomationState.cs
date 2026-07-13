namespace LeadRecovery.Domain.Leads;

public enum AutomationState
{
    Active,
    PausedByUser,
    PausedBySystem,
    Completed,
    SuppressedOptOut,
    SuppressedPolicy,
}
