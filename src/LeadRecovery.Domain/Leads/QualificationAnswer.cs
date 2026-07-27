using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Leads;

public enum QualificationAnswerOutcome
{
    Accepted,
    Unknown,
    Ambiguous,
}

public sealed class QualificationAnswer : ITenantOwnedEntity
{
    private QualificationAnswer()
    {
    }

    public QualificationAnswer(
        Guid id,
        Guid tenantId,
        Guid leadId,
        Guid sourceMessageId,
        string questionKey,
        string? value,
        QualificationAnswerOutcome outcome,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        LeadId = RequireId(leadId, nameof(leadId));
        SourceMessageId = RequireId(sourceMessageId, nameof(sourceMessageId));
        QuestionKey = NormalizeRequired(
            questionKey,
            Automations.WorkflowDefinitionFieldLimits.QuestionKeyMaximumLength,
            nameof(questionKey));
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        Outcome = outcome;
        Value = outcome == QualificationAnswerOutcome.Accepted
            ? NormalizeRequired(
                value,
                Automations.WorkflowDefinitionFieldLimits.AnswerValueMaximumLength,
                nameof(value))
            : null;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid SourceMessageId { get; private set; }
    public string QuestionKey { get; private set; } = string.Empty;
    public string? Value { get; private set; }
    public QualificationAnswerOutcome Outcome { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
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
