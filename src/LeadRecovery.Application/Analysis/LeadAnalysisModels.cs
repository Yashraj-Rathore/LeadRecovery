using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Analysis;

public static class LeadAnalysisSchema
{
    public const string CurrentVersion = "1.0";
    public const string UnknownCategory = "Unknown";
    public const int MaximumAllowedCategories = 50;
    public const int MaximumCategoryLength = 100;
    public const int MaximumConversationTurns = 50;
    public const int MaximumTurnLength = 1_600;
    public const int MaximumServiceAreaRulesLength = 500;
    public const int MaximumSummaryLength = 1_000;
    public const int MaximumExtractedValueLength = 200;
    public const int MaximumReasonCodes = 10;
    public const int MaximumReasonCodeLength = 64;
    public const int MaximumSuggestedReplyLength = 1_000;
    public const double NormalDisplayConfidence = 0.85;
    public const double AutomaticApplicationConfidence = 0.65;
}

public enum ConversationParticipant
{
    Customer,
    Business,
}

public sealed record ConversationTurn
{
    public ConversationTurn(ConversationParticipant participant, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string normalizedText = text.Trim();
        if (normalizedText.Length > LeadAnalysisSchema.MaximumTurnLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Conversation text cannot exceed {LeadAnalysisSchema.MaximumTurnLength} characters.");
        }

        Participant = participant;
        Text = normalizedText;
    }

    public ConversationParticipant Participant { get; }

    public string Text { get; }
}

public sealed class LeadAnalysisRequest
{
    public LeadAnalysisRequest(
        Guid tenantId,
        IEnumerable<string> allowedCategories,
        IEnumerable<ConversationTurn> turns,
        string schemaVersion,
        string? serviceAreaRules = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(allowedCategories);
        ArgumentNullException.ThrowIfNull(turns);
        if (!string.Equals(
                schemaVersion,
                LeadAnalysisSchema.CurrentVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported lead-analysis schema version '{schemaVersion}'.",
                nameof(schemaVersion));
        }

        string[] normalizedCategories = allowedCategories
            .Select(static category =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(category);
                return category.Trim();
            })
            .ToArray();
        if (normalizedCategories.Length is 0 or > LeadAnalysisSchema.MaximumAllowedCategories)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allowedCategories),
                $"Between 1 and {LeadAnalysisSchema.MaximumAllowedCategories} categories are required.");
        }

        if (normalizedCategories.Any(
                category => category.Length > LeadAnalysisSchema.MaximumCategoryLength))
        {
            throw new ArgumentOutOfRangeException(
                nameof(allowedCategories),
                $"A category cannot exceed {LeadAnalysisSchema.MaximumCategoryLength} characters.");
        }

        if (normalizedCategories.Any(category => category.Equals(
                LeadAnalysisSchema.UnknownCategory,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"'{LeadAnalysisSchema.UnknownCategory}' is reserved for insufficient evidence.",
                nameof(allowedCategories));
        }

        if (normalizedCategories.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            normalizedCategories.Length)
        {
            throw new ArgumentException(
                "Allowed categories must be unique ignoring case.",
                nameof(allowedCategories));
        }

        ConversationTurn[] normalizedTurns = turns.ToArray();
        if (normalizedTurns.Length is 0 or > LeadAnalysisSchema.MaximumConversationTurns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(turns),
                $"Between 1 and {LeadAnalysisSchema.MaximumConversationTurns} turns are required.");
        }

        string? normalizedServiceAreaRules = string.IsNullOrWhiteSpace(serviceAreaRules)
            ? null
            : serviceAreaRules.Trim();
        if (normalizedServiceAreaRules?.Length >
            LeadAnalysisSchema.MaximumServiceAreaRulesLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(serviceAreaRules),
                $"Service-area rules cannot exceed {LeadAnalysisSchema.MaximumServiceAreaRulesLength} characters.");
        }

        TenantId = tenantId;
        AllowedCategories = Array.AsReadOnly(normalizedCategories);
        Turns = Array.AsReadOnly(normalizedTurns);
        SchemaVersion = schemaVersion;
        ServiceAreaRules = normalizedServiceAreaRules;
    }

    public Guid TenantId { get; }

    public IReadOnlyList<string> AllowedCategories { get; }

    public IReadOnlyList<ConversationTurn> Turns { get; }

    public string SchemaVersion { get; }

    public string? ServiceAreaRules { get; }
}

public sealed record LeadAnalysisExtractedFields(
    string? City,
    string? PostalCode,
    string? PreferredCallbackWindow);

public sealed record LeadAnalysisSuggestion(
    string SchemaVersion,
    string ServiceCategory,
    LeadUrgency Urgency,
    string Summary,
    LeadAnalysisExtractedFields Extracted,
    double Confidence,
    bool RequiresHumanReview,
    IReadOnlyList<string> ReasonCodes,
    string? SuggestedReply);

public enum LeadAnalysisFailureKind
{
    Timeout,
    TransientProvider,
    ProviderRejected,
    Refused,
    InvalidOutput,
}

public sealed record LeadAnalysisFailure(
    LeadAnalysisFailureKind Kind,
    string Code,
    bool IsRetryable);

public sealed record LeadAnalysisResult
{
    private LeadAnalysisResult(
        string provider,
        string modelReference,
        int attemptCount,
        LeadAnalysisSuggestion? suggestion,
        LeadAnalysisFailure? failure)
    {
        Provider = provider;
        ModelReference = modelReference;
        AttemptCount = attemptCount;
        Suggestion = suggestion;
        Failure = failure;
    }

    public string Provider { get; }

    public string ModelReference { get; }

    public int AttemptCount { get; }

    public LeadAnalysisSuggestion? Suggestion { get; }

    public LeadAnalysisFailure? Failure { get; }

    public bool Succeeded => Suggestion is not null && Failure is null;

    public static LeadAnalysisResult Success(
        string provider,
        string modelReference,
        int attemptCount,
        LeadAnalysisSuggestion suggestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptCount, 1);
        ArgumentNullException.ThrowIfNull(suggestion);
        return new LeadAnalysisResult(
            provider,
            modelReference,
            attemptCount,
            suggestion,
            null);
    }

    public static LeadAnalysisResult Failed(
        string provider,
        string modelReference,
        int attemptCount,
        LeadAnalysisFailure failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptCount, 1);
        ArgumentNullException.ThrowIfNull(failure);
        return new LeadAnalysisResult(
            provider,
            modelReference,
            attemptCount,
            null,
            failure);
    }
}

public interface ILeadAnalysisService
{
    Task<LeadAnalysisResult> AnalyzeAsync(
        LeadAnalysisRequest request,
        CancellationToken cancellationToken);
}
