using System.Text.Json;
using System.Text.Json.Serialization;

using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Automations;

public sealed class WorkflowDefinition : ITenantOwnedEntity
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private WorkflowDefinition()
    {
    }

    public WorkflowDefinition(
        Guid id,
        Guid tenantId,
        string name,
        int version,
        string bookingUrl,
        IReadOnlyCollection<QualificationQuestionPolicy> qualificationQuestions,
        BusinessHoursPolicy businessHours,
        IReadOnlyCollection<FollowUpStepPolicy> followUpSteps,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        Name = NormalizeRequired(
            name,
            WorkflowDefinitionFieldLimits.NameMaximumLength,
            nameof(name));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Version = version;
        BookingUrl = NormalizeBookingUrl(bookingUrl);
        QualificationPolicyJson = SerializeQuestions(qualificationQuestions);
        BusinessHoursPolicyJson = SerializeBusinessHours(businessHours);
        FollowUpPolicyJson = SerializeFollowUps(followUpSteps);
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public bool IsActive { get; private set; }

    public string BookingUrl { get; private set; } = string.Empty;

    public string FollowUpPolicyJson { get; private set; } = string.Empty;

    public string BusinessHoursPolicyJson { get; private set; } = string.Empty;

    public string QualificationPolicyJson { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Activate(DateTimeOffset activatedAtUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAtUtc = RequireCurrentOrLaterUtc(activatedAtUtc);
    }

    public void Deactivate(DateTimeOffset deactivatedAtUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAtUtc = RequireCurrentOrLaterUtc(deactivatedAtUtc);
    }

    public QualificationQuestionPolicy[] GetQualificationQuestions() =>
        Deserialize<QualificationQuestionPolicy[]>(QualificationPolicyJson);

    public BusinessHoursPolicy GetBusinessHoursPolicy() =>
        Deserialize<BusinessHoursPolicy>(BusinessHoursPolicyJson);

    public FollowUpStepPolicy[] GetFollowUpSteps() =>
        Deserialize<FollowUpStepPolicy[]>(FollowUpPolicyJson);

    private static string SerializeQuestions(
        IReadOnlyCollection<QualificationQuestionPolicy> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count is < 1 or > WorkflowDefinitionFieldLimits.MaximumQuestions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(questions),
                $"A workflow requires 1 through " +
                $"{WorkflowDefinitionFieldLimits.MaximumQuestions} questions.");
        }

        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        QualificationQuestionPolicy[] normalized = questions.Select(question =>
        {
            ArgumentNullException.ThrowIfNull(question);
            string key = NormalizeRequired(
                question.Key,
                WorkflowDefinitionFieldLimits.QuestionKeyMaximumLength,
                nameof(question.Key));
            if (!keys.Add(key))
            {
                throw new ArgumentException("Qualification question keys must be unique.");
            }

            string prompt = NormalizeRequired(
                question.Prompt,
                WorkflowDefinitionFieldLimits.QuestionPromptMaximumLength,
                nameof(question.Prompt));
            if (!Enum.IsDefined(question.AnswerKind))
            {
                throw new ArgumentOutOfRangeException(nameof(questions));
            }

            string[] allowedValues = question.AllowedValues?
                .Select(value => NormalizeRequired(
                    value,
                    WorkflowDefinitionFieldLimits.AnswerValueMaximumLength,
                    nameof(question.AllowedValues)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            if (question.AnswerKind == QualificationAnswerKind.Choice &&
                allowedValues.Length < 2)
            {
                throw new ArgumentException(
                    "A choice question requires at least two distinct allowed values.");
            }

            if (question.AnswerKind == QualificationAnswerKind.RequiredText &&
                allowedValues.Length != 0)
            {
                throw new ArgumentException(
                    "A required-text question cannot define allowed values.");
            }

            return new QualificationQuestionPolicy(
                key,
                prompt,
                question.AnswerKind,
                allowedValues);
        }).ToArray();
        return Serialize(normalized, nameof(questions));
    }

    private static string SerializeBusinessHours(BusinessHoursPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policy.Windows);
        if (policy.Windows.Length == 0)
        {
            throw new ArgumentException("At least one business-hours window is required.");
        }

        if (policy.Windows.GroupBy(window => window.Day).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Only one business-hours window is allowed per day.");
        }

        foreach (BusinessDayHours window in policy.Windows)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (!Enum.IsDefined(window.Day) || window.OpensAt >= window.ClosesAt)
            {
                throw new ArgumentException(
                    "Business-hours windows require a defined day and an opening before closing.");
            }
        }

        BusinessHoursPolicy normalized = new(
            policy.Windows.OrderBy(window => window.Day).ToArray(),
            policy.UrgentHumanReviewAfterHours);
        return Serialize(normalized, nameof(policy));
    }

    private static string SerializeFollowUps(
        IReadOnlyCollection<FollowUpStepPolicy> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count > WorkflowDefinitionFieldLimits.MaximumFollowUps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(steps),
                $"A workflow supports at most " +
                $"{WorkflowDefinitionFieldLimits.MaximumFollowUps} follow-ups.");
        }

        FollowUpStepPolicy[] normalized = steps
            .OrderBy(step => step.Sequence)
            .Select(step =>
            {
                ArgumentNullException.ThrowIfNull(step);
                ArgumentOutOfRangeException.ThrowIfLessThan(step.Sequence, 1);
                ArgumentOutOfRangeException.ThrowIfLessThan(step.DelayMinutes, 1);
                string purpose = NormalizeRequired(
                    step.TemplatePurpose,
                    WorkflowDefinitionFieldLimits.TemplatePurposeMaximumLength,
                    nameof(step.TemplatePurpose));
                return new FollowUpStepPolicy(step.Sequence, step.DelayMinutes, purpose);
            })
            .ToArray();
        if (normalized.Select(step => step.Sequence).Distinct().Count() != normalized.Length ||
            normalized.Select(step => step.TemplatePurpose)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new ArgumentException(
                "Follow-up sequence numbers and template purposes must be unique.");
        }

        return Serialize(normalized, nameof(steps));
    }

    private static string NormalizeBookingUrl(string value)
    {
        string normalized = NormalizeRequired(
            value,
            WorkflowDefinitionFieldLimits.BookingUrlMaximumLength,
            nameof(value));
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "The booking URL must be an absolute HTTPS URL without embedded credentials.",
                nameof(value));
        }

        return uri.AbsoluteUri;
    }

    private static string Serialize<T>(T value, string parameterName)
    {
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        if (json.Length > WorkflowDefinitionFieldLimits.PolicyJsonMaximumLength)
        {
            throw new ArgumentException("The workflow policy is too large.", parameterName);
        }

        return json;
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions) ??
        throw new InvalidOperationException("The persisted workflow policy is invalid.");

    private DateTimeOffset RequireCurrentOrLaterUtc(DateTimeOffset value)
    {
        DateTimeOffset utc = RequireUtc(value, nameof(value));
        if (utc < UpdatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return utc;
    }

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
