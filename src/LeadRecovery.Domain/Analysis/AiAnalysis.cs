using System.Text.Json;
using System.Text.Json.Serialization;

using LeadRecovery.Domain.Common;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Domain.Analysis;

public sealed class AiAnalysis : ITenantOwnedEntity
{
    private const string UnknownCategory = "Unknown";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private AiAnalysis()
    {
    }

    public AiAnalysis(
        Guid id,
        Guid tenantId,
        Guid leadId,
        string schemaVersion,
        string provider,
        string modelReference,
        string inputHash,
        IReadOnlyCollection<string> allowedCategories,
        AiAnalysisValues suggestion,
        double confidence,
        bool requiresHumanReview,
        IReadOnlyCollection<string> reasonCodes,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        LeadId = RequireId(leadId, nameof(leadId));
        SchemaVersion = NormalizeRequired(
            schemaVersion,
            AiAnalysisFieldLimits.SchemaVersionMaximumLength,
            nameof(schemaVersion));
        Provider = NormalizeRequired(
            provider,
            AiAnalysisFieldLimits.ProviderMaximumLength,
            nameof(provider));
        ModelReference = NormalizeRequired(
            modelReference,
            AiAnalysisFieldLimits.ModelReferenceMaximumLength,
            nameof(modelReference));
        InputHash = NormalizeInputHash(inputHash);
        AllowedCategoriesJson = SerializeAllowedCategories(allowedCategories);
        ArgumentNullException.ThrowIfNull(suggestion);
        AiAnalysisValues normalizedSuggestion = NormalizeValues(
            suggestion,
            GetAllowedCategories());
        CategorySuggestion = normalizedSuggestion.ServiceCategory;
        UrgencySuggestion = normalizedSuggestion.Urgency;
        Summary = normalizedSuggestion.Summary;
        ExtractedCity = normalizedSuggestion.City;
        ExtractedPostalCode = normalizedSuggestion.PostalCode;
        ExtractedPreferredCallbackWindow = normalizedSuggestion.PreferredCallbackWindow;
        SuggestedReply = normalizedSuggestion.SuggestedReply;
        Confidence = RequireConfidence(confidence);
        RequiresHumanReview = requiresHumanReview;
        ReasonCodesJson = SerializeReasonCodes(reasonCodes);
        RawStructuredOutputJson = SerializeJson(
            new
            {
                schemaVersion = SchemaVersion,
                serviceCategory = CategorySuggestion,
                urgency = UrgencySuggestion,
                summary = Summary,
                extracted = new
                {
                    city = ExtractedCity,
                    postalCode = ExtractedPostalCode,
                    preferredCallbackWindow = ExtractedPreferredCallbackWindow,
                },
                confidence = Confidence,
                requiresHumanReview = RequiresHumanReview,
                reasonCodes = GetReasonCodes(),
                suggestedReply = SuggestedReply,
            },
            nameof(suggestion));
        ReviewStatus = AiAnalysisReviewStatus.Pending;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid LeadId { get; private set; }

    public string SchemaVersion { get; private set; } = string.Empty;

    public string Provider { get; private set; } = string.Empty;

    public string ModelReference { get; private set; } = string.Empty;

    public string InputHash { get; private set; } = string.Empty;

    public string AllowedCategoriesJson { get; private set; } = string.Empty;

    public string CategorySuggestion { get; private set; } = string.Empty;

    public LeadUrgency UrgencySuggestion { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string? ExtractedCity { get; private set; }

    public string? ExtractedPostalCode { get; private set; }

    public string? ExtractedPreferredCallbackWindow { get; private set; }

    public string? SuggestedReply { get; private set; }

    public double Confidence { get; private set; }

    public bool RequiresHumanReview { get; private set; }

    public string ReasonCodesJson { get; private set; } = string.Empty;

    public string RawStructuredOutputJson { get; private set; } = string.Empty;

    public AiAnalysisReviewStatus ReviewStatus { get; private set; }

    public string? ReviewedCategory { get; private set; }

    public LeadUrgency? ReviewedUrgency { get; private set; }

    public string? ReviewedSummary { get; private set; }

    public string? ReviewedCity { get; private set; }

    public string? ReviewedPostalCode { get; private set; }

    public string? ReviewedPreferredCallbackWindow { get; private set; }

    public string? ReviewedSuggestedReply { get; private set; }

    public string? CorrectionReason { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<string> GetAllowedCategories() =>
        DeserializeArray(AllowedCategoriesJson);

    public IReadOnlyList<string> GetReasonCodes() =>
        DeserializeArray(ReasonCodesJson);

    public AiAnalysisValues GetSuggestion() =>
        new(
            CategorySuggestion,
            UrgencySuggestion,
            Summary,
            ExtractedCity,
            ExtractedPostalCode,
            ExtractedPreferredCallbackWindow,
            SuggestedReply);

    public AiAnalysisValues? GetReviewedValues() =>
        ReviewStatus is AiAnalysisReviewStatus.Accepted or AiAnalysisReviewStatus.Edited
            ? new AiAnalysisValues(
                ReviewedCategory ??
                    throw new InvalidOperationException("Reviewed category is missing."),
                ReviewedUrgency ??
                    throw new InvalidOperationException("Reviewed urgency is missing."),
                ReviewedSummary ??
                    throw new InvalidOperationException("Reviewed summary is missing."),
                ReviewedCity,
                ReviewedPostalCode,
                ReviewedPreferredCallbackWindow,
                ReviewedSuggestedReply)
            : null;

    public void Accept(
        Guid reviewerUserId,
        string? correctionReason,
        DateTimeOffset reviewedAtUtc)
    {
        EnsurePending();
        ApplyReviewedValues(GetSuggestion());
        CompleteReview(
            AiAnalysisReviewStatus.Accepted,
            reviewerUserId,
            correctionReason,
            reviewedAtUtc);
    }

    public void Edit(
        Guid reviewerUserId,
        AiAnalysisValues values,
        string? correctionReason,
        DateTimeOffset reviewedAtUtc)
    {
        EnsurePending();
        ArgumentNullException.ThrowIfNull(values);
        ApplyReviewedValues(NormalizeValues(values, GetAllowedCategories()));
        CompleteReview(
            AiAnalysisReviewStatus.Edited,
            reviewerUserId,
            correctionReason,
            reviewedAtUtc);
    }

    public void Reject(
        Guid reviewerUserId,
        string? correctionReason,
        DateTimeOffset reviewedAtUtc)
    {
        EnsurePending();
        CompleteReview(
            AiAnalysisReviewStatus.Rejected,
            reviewerUserId,
            correctionReason,
            reviewedAtUtc);
    }

    private void ApplyReviewedValues(AiAnalysisValues values)
    {
        ReviewedCategory = values.ServiceCategory;
        ReviewedUrgency = values.Urgency;
        ReviewedSummary = values.Summary;
        ReviewedCity = values.City;
        ReviewedPostalCode = values.PostalCode;
        ReviewedPreferredCallbackWindow = values.PreferredCallbackWindow;
        ReviewedSuggestedReply = values.SuggestedReply;
    }

    private void CompleteReview(
        AiAnalysisReviewStatus status,
        Guid reviewerUserId,
        string? correctionReason,
        DateTimeOffset reviewedAtUtc)
    {
        ReviewedByUserId = RequireId(reviewerUserId, nameof(reviewerUserId));
        DateTimeOffset reviewedAt = RequireUtc(reviewedAtUtc, nameof(reviewedAtUtc));
        if (reviewedAt < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reviewedAtUtc),
                "A review cannot predate the analysis.");
        }

        CorrectionReason = NormalizeOptional(
            correctionReason,
            AiAnalysisFieldLimits.CorrectionReasonMaximumLength,
            nameof(correctionReason));
        ReviewStatus = status;
        ReviewedAtUtc = reviewedAt;
    }

    private void EnsurePending()
    {
        if (ReviewStatus != AiAnalysisReviewStatus.Pending)
        {
            throw new InvalidOperationException(
                "A completed AI analysis review cannot be changed.");
        }
    }

    private static AiAnalysisValues NormalizeValues(
        AiAnalysisValues values,
        IReadOnlyList<string> allowedCategories)
    {
        string category = NormalizeRequired(
            values.ServiceCategory,
            AiAnalysisFieldLimits.CategoryMaximumLength,
            nameof(values));
        if (!category.Equals(UnknownCategory, StringComparison.Ordinal) &&
            !allowedCategories.Contains(category, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "The service category is not in the analysis category snapshot.",
                nameof(values));
        }

        if (!Enum.IsDefined(values.Urgency))
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }

        return new AiAnalysisValues(
            category,
            values.Urgency,
            NormalizeRequired(
                values.Summary,
                AiAnalysisFieldLimits.SummaryMaximumLength,
                nameof(values)),
            NormalizeOptional(
                values.City,
                AiAnalysisFieldLimits.ExtractedValueMaximumLength,
                nameof(values)),
            NormalizeOptional(
                values.PostalCode,
                AiAnalysisFieldLimits.ExtractedValueMaximumLength,
                nameof(values)),
            NormalizeOptional(
                values.PreferredCallbackWindow,
                AiAnalysisFieldLimits.ExtractedValueMaximumLength,
                nameof(values)),
            NormalizeOptional(
                values.SuggestedReply,
                AiAnalysisFieldLimits.SuggestedReplyMaximumLength,
                nameof(values)));
    }

    private static string SerializeAllowedCategories(
        IReadOnlyCollection<string> allowedCategories)
    {
        ArgumentNullException.ThrowIfNull(allowedCategories);
        if (allowedCategories.Count is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allowedCategories),
                "Between 1 and 50 allowed categories are required.");
        }

        string[] normalized = allowedCategories
            .Select(category => NormalizeRequired(
                category,
                AiAnalysisFieldLimits.CategoryMaximumLength,
                nameof(allowedCategories)))
            .ToArray();
        if (normalized.Any(
                category => category.Equals(UnknownCategory, StringComparison.OrdinalIgnoreCase)) ||
            normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new ArgumentException(
                "Allowed categories must be unique and cannot contain the reserved Unknown value.",
                nameof(allowedCategories));
        }

        return SerializeJson(normalized, nameof(allowedCategories));
    }

    private static string SerializeReasonCodes(IReadOnlyCollection<string> reasonCodes)
    {
        ArgumentNullException.ThrowIfNull(reasonCodes);
        if (reasonCodes.Count > AiAnalysisFieldLimits.MaximumReasonCodes)
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCodes));
        }

        string[] normalized = reasonCodes
            .Select(code => NormalizeRequired(
                code,
                AiAnalysisFieldLimits.ReasonCodeMaximumLength,
                nameof(reasonCodes)))
            .ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length ||
            normalized.Any(code => !IsReasonCode(code)))
        {
            throw new ArgumentException(
                "Reason codes must be unique uppercase identifiers.",
                nameof(reasonCodes));
        }

        return SerializeJson(normalized, nameof(reasonCodes));
    }

    private static string SerializeJson<T>(T value, string parameterName)
    {
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        if (json.Length > AiAnalysisFieldLimits.JsonMaximumLength)
        {
            throw new ArgumentException(
                $"JSON cannot exceed {AiAnalysisFieldLimits.JsonMaximumLength} characters.",
                parameterName);
        }

        return json;
    }

    private static string[] DeserializeArray(string json) =>
        JsonSerializer.Deserialize<string[]>(json, SerializerOptions) ??
        throw new InvalidOperationException("Persisted AI analysis JSON is invalid.");

    private static string NormalizeInputHash(string? value)
    {
        string normalized = NormalizeRequired(
            value,
            AiAnalysisFieldLimits.InputHashLength,
            nameof(value));
        if (normalized.Length != AiAnalysisFieldLimits.InputHashLength ||
            normalized.Any(character => character is not (
                >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "The input hash must be a lowercase SHA-256 hexadecimal value.",
                nameof(value));
        }

        return normalized;
    }

    private static double RequireConfidence(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Confidence must be between zero and one.");
        }

        return value;
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

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, parameterName);

    private static bool IsReasonCode(string value) =>
        value[0] is >= 'A' and <= 'Z' &&
        value.Skip(1).All(
            static character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", parameterName);
        }

        return value;
    }
}
