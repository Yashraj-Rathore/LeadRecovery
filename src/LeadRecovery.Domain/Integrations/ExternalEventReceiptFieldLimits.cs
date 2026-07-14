namespace LeadRecovery.Domain.Integrations;

public static class ExternalEventReceiptFieldLimits
{
    public const int ProviderMaximumLength = 50;
    public const int EventTypeMaximumLength = 100;
    public const int ExternalEventIdMaximumLength = 200;
    public const int PayloadHashMaximumLength = 128;
    public const int ProcessingResultMaximumLength = 500;
}
