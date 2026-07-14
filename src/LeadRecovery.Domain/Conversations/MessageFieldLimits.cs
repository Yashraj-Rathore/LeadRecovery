namespace LeadRecovery.Domain.Conversations;

public static class MessageFieldLimits
{
    public const int ProviderMaximumLength = 50;
    public const int ProviderMessageSidMaximumLength = 100;
    public const int ClientIdempotencyKeyMaximumLength = 200;
    public const int BodyMaximumLength = 1600;
    public const int FailureCodeMaximumLength = 100;
    public const int FailureDescriptionMaximumLength = 500;
}
