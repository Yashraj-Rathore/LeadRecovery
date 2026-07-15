using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Conversations;

public sealed class MessageTemplate : ITenantOwnedEntity
{
    private MessageTemplate()
    {
    }

    public MessageTemplate(
        Guid id,
        Guid tenantId,
        string name,
        string purpose,
        string body,
        int version,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        Name = Normalize(name, MessageTemplateFieldLimits.NameMaximumLength, nameof(name));
        Purpose = Normalize(
            purpose,
            MessageTemplateFieldLimits.PurposeMaximumLength,
            nameof(purpose));
        Body = Normalize(body, MessageTemplateFieldLimits.BodyMaximumLength, nameof(body));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Version = version;
        CreatedByUserId = RequireId(createdByUserId, nameof(createdByUserId));
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Purpose { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public bool IsApproved { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public void Approve(Guid approvedByUserId, DateTimeOffset approvedAtUtc)
    {
        if (IsApproved)
        {
            throw new InvalidOperationException("The template is already approved.");
        }

        DateTimeOffset utcTimestamp = RequireUtc(approvedAtUtc, nameof(approvedAtUtc));
        if (utcTimestamp < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(approvedAtUtc));
        }

        ApprovedByUserId = RequireId(approvedByUserId, nameof(approvedByUserId));
        ApprovedAtUtc = utcTimestamp;
        IsApproved = true;
    }

    public void Activate()
    {
        if (!IsApproved)
        {
            throw new InvalidOperationException("Only approved templates can be activated.");
        }

        IsActive = true;
    }

    public void Deactivate() => IsActive = false;

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
    }

    private static string Normalize(string? value, int maximumLength, string parameterName)
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
