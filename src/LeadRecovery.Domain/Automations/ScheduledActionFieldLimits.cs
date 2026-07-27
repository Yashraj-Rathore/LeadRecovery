namespace LeadRecovery.Domain.Automations;

public static class ScheduledActionFieldLimits
{
    public const int ActionTypeMaximumLength = 100;
    public const int IdempotencyKeyMaximumLength = 200;
    public const int PayloadJsonMaximumLength = 16_384;
    public const int LastErrorMaximumLength = 1_000;
    public const int CorrelationIdMaximumLength = 100;
    public const int TraceParentMaximumLength = 55;
    public const int TraceStateMaximumLength = 512;
}
