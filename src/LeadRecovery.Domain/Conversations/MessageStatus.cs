namespace LeadRecovery.Domain.Conversations;

public enum MessageStatus
{
    Queued,
    Sent,
    Delivered,
    Failed,
    Received,
    Suppressed,
}
