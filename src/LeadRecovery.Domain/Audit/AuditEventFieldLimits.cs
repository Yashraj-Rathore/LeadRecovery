namespace LeadRecovery.Domain.Audit;

public static class AuditEventFieldLimits
{
    public const int ActorTypeMaximumLength = 50;
    public const int ActorIdMaximumLength = 100;
    public const int ActionMaximumLength = 100;
    public const int EntityTypeMaximumLength = 100;
    public const int EntityIdMaximumLength = 100;
    public const int JsonMaximumLength = 16_384;
    public const int CorrelationIdMaximumLength = 100;
}
