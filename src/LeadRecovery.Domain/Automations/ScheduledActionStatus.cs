namespace LeadRecovery.Domain.Automations;

public enum ScheduledActionStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed,
}
