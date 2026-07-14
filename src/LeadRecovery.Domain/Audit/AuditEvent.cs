using System.Text.Json;

namespace LeadRecovery.Domain.Audit;

public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public AuditEvent(
        Guid id,
        Guid? tenantId,
        string actorType,
        string? actorId,
        string action,
        string entityType,
        string entityId,
        string correlationId,
        DateTimeOffset createdAtUtc,
        string? beforeJson = null,
        string? afterJson = null)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireOptionalId(tenantId, nameof(tenantId));
        ActorType = NormalizeRequired(
            actorType,
            AuditEventFieldLimits.ActorTypeMaximumLength,
            nameof(actorType));
        ActorId = NormalizeOptional(
            actorId,
            AuditEventFieldLimits.ActorIdMaximumLength,
            nameof(actorId));
        Action = NormalizeRequired(
            action,
            AuditEventFieldLimits.ActionMaximumLength,
            nameof(action));
        EntityType = NormalizeRequired(
            entityType,
            AuditEventFieldLimits.EntityTypeMaximumLength,
            nameof(entityType));
        EntityId = NormalizeRequired(
            entityId,
            AuditEventFieldLimits.EntityIdMaximumLength,
            nameof(entityId));
        CorrelationId = NormalizeRequired(
            correlationId,
            AuditEventFieldLimits.CorrelationIdMaximumLength,
            nameof(correlationId));
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        BeforeJson = NormalizeJson(beforeJson, nameof(beforeJson));
        AfterJson = NormalizeJson(afterJson, nameof(afterJson));
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public string ActorType { get; private set; } = string.Empty;

    public string? ActorId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string? BeforeJson { get; private set; }

    public string? AfterJson { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
    }

    private static Guid? RequireOptionalId(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An optional ID cannot be empty.", parameterName);
        }

        return value;
    }

    private static string NormalizeRequired(
        string? value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, parameterName);

    private static string? NormalizeJson(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "JSON must be null or contain an object.",
                parameterName);
        }

        if (value.Length > AuditEventFieldLimits.JsonMaximumLength)
        {
            throw new ArgumentException(
                $"JSON cannot exceed {AuditEventFieldLimits.JsonMaximumLength} characters.",
                parameterName);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("JSON must contain an object.", parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The value must contain valid JSON.", parameterName, exception);
        }

        return value;
    }

    private static DateTimeOffset RequireUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", parameterName);
        }

        return value;
    }
}
