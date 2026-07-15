using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Leads;

public sealed class LeadNote : ITenantOwnedEntity
{
    private LeadNote()
    {
    }

    public LeadNote(
        Guid id,
        Guid tenantId,
        Guid leadId,
        Guid authorUserId,
        string body,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        LeadId = RequireId(leadId, nameof(leadId));
        AuthorUserId = RequireId(authorUserId, nameof(authorUserId));
        Body = NormalizeBody(body);
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid LeadId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
    }

    private static string NormalizeBody(string? body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        string normalized = body.Trim();
        if (normalized.Length > LeadNoteFieldLimits.BodyMaximumLength)
        {
            throw new ArgumentException(
                $"A lead note cannot exceed {LeadNoteFieldLimits.BodyMaximumLength} characters.",
                nameof(body));
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
