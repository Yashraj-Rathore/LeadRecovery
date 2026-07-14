namespace LeadRecovery.Domain.Automations;

public static class ScheduledActionStatusTransitionPolicy
{
    public static bool CanTransition(
        ScheduledActionStatus current,
        ScheduledActionStatus target)
    {
        if (!Enum.IsDefined(current))
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }

        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        return current switch
        {
            ScheduledActionStatus.Pending => target is ScheduledActionStatus.Running
                or ScheduledActionStatus.Cancelled,
            ScheduledActionStatus.Running => target is ScheduledActionStatus.Pending
                or ScheduledActionStatus.Completed
                or ScheduledActionStatus.Failed,
            ScheduledActionStatus.Completed or
            ScheduledActionStatus.Cancelled or
            ScheduledActionStatus.Failed => false,
            _ => false,
        };
    }
}
