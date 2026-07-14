namespace LeadRecovery.Domain.Integrations;

public sealed class ExternalEventReceipt
{
    private ExternalEventReceipt()
    {
    }

    public ExternalEventReceipt(
        Guid id,
        Guid? tenantId,
        string provider,
        string eventType,
        string externalEventId,
        string payloadHash,
        DateTimeOffset receivedAtUtc)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireOptionalId(tenantId, nameof(tenantId));
        Provider = NormalizeRequired(
            provider,
            ExternalEventReceiptFieldLimits.ProviderMaximumLength,
            nameof(provider));
        EventType = NormalizeRequired(
            eventType,
            ExternalEventReceiptFieldLimits.EventTypeMaximumLength,
            nameof(eventType));
        ExternalEventId = NormalizeRequired(
            externalEventId,
            ExternalEventReceiptFieldLimits.ExternalEventIdMaximumLength,
            nameof(externalEventId));
        PayloadHash = NormalizeRequired(
            payloadHash,
            ExternalEventReceiptFieldLimits.PayloadHashMaximumLength,
            nameof(payloadHash));
        ReceivedAtUtc = RequireUtc(receivedAtUtc, nameof(receivedAtUtc));
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public string ExternalEventId { get; private set; } = string.Empty;

    public string PayloadHash { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public string? ProcessingResult { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        Guid assignedTenantId = RequireId(tenantId, nameof(tenantId));
        if (TenantId == assignedTenantId)
        {
            return;
        }

        if (TenantId is not null)
        {
            throw new InvalidOperationException(
                "The external event receipt tenant cannot be changed.");
        }

        TenantId = assignedTenantId;
    }

    public void MarkProcessed(string processingResult, DateTimeOffset processedAtUtc)
    {
        if (ProcessedAtUtc is not null)
        {
            throw new InvalidOperationException(
                "The external event receipt is already processed.");
        }

        string normalizedResult = NormalizeRequired(
            processingResult,
            ExternalEventReceiptFieldLimits.ProcessingResultMaximumLength,
            nameof(processingResult));
        DateTimeOffset utcTimestamp = RequireUtc(processedAtUtc, nameof(processedAtUtc));
        if (utcTimestamp < ReceivedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processedAtUtc),
                "The processed timestamp cannot precede receipt.");
        }

        ProcessingResult = normalizedResult;
        ProcessedAtUtc = utcTimestamp;
    }

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

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", parameterName);
        }

        return value;
    }
}
