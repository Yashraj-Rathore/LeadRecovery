namespace LeadRecovery.Domain.Conversations;

public static class MessageStatusTransitionPolicy
{
    public static bool CanTransition(MessageStatus current, MessageStatus target)
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
            MessageStatus.Queued => target is MessageStatus.Sent
                or MessageStatus.Failed
                or MessageStatus.Suppressed,
            MessageStatus.Sent => target is MessageStatus.Delivered
                or MessageStatus.Failed,
            MessageStatus.Delivered or
            MessageStatus.Failed or
            MessageStatus.Received or
            MessageStatus.Suppressed => false,
            _ => false,
        };
    }
}
