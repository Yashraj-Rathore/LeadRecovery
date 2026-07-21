using System.Text.Json;

using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Analysis;

public sealed record LeadAnalysisValidationResult(
    LeadAnalysisSuggestion? Suggestion,
    string? FailureCode)
{
    public bool IsValid => Suggestion is not null && FailureCode is null;

    public static LeadAnalysisValidationResult Valid(LeadAnalysisSuggestion suggestion) =>
        new(suggestion, null);

    public static LeadAnalysisValidationResult Invalid(string failureCode) =>
        new(null, failureCode);
}

public interface ILeadAnalysisResultValidator
{
    LeadAnalysisValidationResult Validate(
        string structuredOutputJson,
        LeadAnalysisRequest request);
}

public sealed class LeadAnalysisResultValidator : ILeadAnalysisResultValidator
{
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "serviceCategory",
        "urgency",
        "summary",
        "extracted",
        "confidence",
        "requiresHumanReview",
        "reasonCodes",
        "suggestedReply",
    ];

    private static readonly string[] ExtractedProperties =
    [
        "city",
        "postalCode",
        "preferredCallbackWindow",
    ];

    private static readonly HashSet<string> SafetySensitiveReasonCodes =
        new(StringComparer.Ordinal)
        {
            "ACTIVE_PROPERTY_DAMAGE",
            "CARBON_MONOXIDE",
            "ELECTRICAL_HAZARD",
            "FLOODING",
            "GAS_ODOR",
            "IMMEDIATE_DANGER",
            "MEDICAL_EMERGENCY",
        };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8,
    };

    public LeadAnalysisValidationResult Validate(
        string structuredOutputJson,
        LeadAnalysisRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(structuredOutputJson);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                structuredOutputJson,
                DocumentOptions);
            JsonElement root = document.RootElement;
            if (!HasExactProperties(root, RootProperties) ||
                !TryGetRequiredString(root, "schemaVersion", out string? schemaVersion) ||
                !string.Equals(schemaVersion, request.SchemaVersion, StringComparison.Ordinal) ||
                !TryGetRequiredString(root, "serviceCategory", out string? serviceCategory) ||
                !IsAllowedCategory(serviceCategory!, request.AllowedCategories) ||
                !TryGetRequiredString(root, "urgency", out string? urgencyValue) ||
                !Enum.TryParse(urgencyValue, ignoreCase: false, out LeadUrgency urgency) ||
                !Enum.IsDefined(urgency) ||
                !TryGetBoundedString(
                    root,
                    "summary",
                    LeadAnalysisSchema.MaximumSummaryLength,
                    allowNull: false,
                    out string? summary) ||
                string.IsNullOrWhiteSpace(summary) ||
                !root.TryGetProperty("extracted", out JsonElement extractedElement) ||
                !TryReadExtracted(extractedElement, out LeadAnalysisExtractedFields? extracted) ||
                !TryGetConfidence(root, out double confidence) ||
                !root.TryGetProperty("requiresHumanReview", out JsonElement reviewElement) ||
                reviewElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
                !TryReadReasonCodes(root, out IReadOnlyList<string>? reasonCodes) ||
                !TryGetBoundedString(
                    root,
                    "suggestedReply",
                    LeadAnalysisSchema.MaximumSuggestedReplyLength,
                    allowNull: true,
                    out string? suggestedReply))
            {
                return LeadAnalysisValidationResult.Invalid("schema_validation_failed");
            }

            bool requiresHumanReview = reviewElement.GetBoolean() ||
                confidence < LeadAnalysisSchema.NormalDisplayConfidence ||
                reasonCodes!.Any(SafetySensitiveReasonCodes.Contains);

            LeadAnalysisSuggestion suggestion = new(
                schemaVersion!,
                serviceCategory!,
                urgency,
                summary!,
                extracted!,
                confidence,
                requiresHumanReview,
                reasonCodes!,
                suggestedReply);
            return LeadAnalysisValidationResult.Valid(suggestion);
        }
        catch (JsonException)
        {
            return LeadAnalysisValidationResult.Invalid("invalid_json");
        }
    }

    private static bool TryReadExtracted(
        JsonElement element,
        out LeadAnalysisExtractedFields? extracted)
    {
        if (!HasExactProperties(element, ExtractedProperties) ||
            !TryGetBoundedString(
                element,
                "city",
                LeadAnalysisSchema.MaximumExtractedValueLength,
                allowNull: true,
                out string? city) ||
            !TryGetBoundedString(
                element,
                "postalCode",
                LeadAnalysisSchema.MaximumExtractedValueLength,
                allowNull: true,
                out string? postalCode) ||
            !TryGetBoundedString(
                element,
                "preferredCallbackWindow",
                LeadAnalysisSchema.MaximumExtractedValueLength,
                allowNull: true,
                out string? preferredCallbackWindow))
        {
            extracted = null;
            return false;
        }

        extracted = new LeadAnalysisExtractedFields(
            city,
            postalCode,
            preferredCallbackWindow);
        return true;
    }

    private static bool TryReadReasonCodes(
        JsonElement root,
        out IReadOnlyList<string>? reasonCodes)
    {
        reasonCodes = null;
        if (!root.TryGetProperty("reasonCodes", out JsonElement element) ||
            element.ValueKind != JsonValueKind.Array ||
            element.GetArrayLength() > LeadAnalysisSchema.MaximumReasonCodes)
        {
            return false;
        }

        List<string> values = [];
        HashSet<string> uniqueValues = new(StringComparer.Ordinal);
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string? value = item.GetString();
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > LeadAnalysisSchema.MaximumReasonCodeLength ||
                !IsReasonCode(value) ||
                !uniqueValues.Add(value))
            {
                return false;
            }

            values.Add(value);
        }

        reasonCodes = values.AsReadOnly();
        return true;
    }

    private static bool TryGetConfidence(JsonElement root, out double confidence)
    {
        confidence = default;
        return root.TryGetProperty("confidence", out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out confidence) &&
            double.IsFinite(confidence) &&
            confidence is >= 0 and <= 1;
    }

    private static bool TryGetRequiredString(
        JsonElement root,
        string propertyName,
        out string? value) =>
        TryGetBoundedString(
            root,
            propertyName,
            LeadAnalysisSchema.MaximumCategoryLength,
            allowNull: false,
            out value) &&
        !string.IsNullOrWhiteSpace(value);

    private static bool TryGetBoundedString(
        JsonElement root,
        string propertyName,
        int maximumLength,
        bool allowNull,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return allowNull;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? candidate = element.GetString();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > maximumLength)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool HasExactProperties(
        JsonElement element,
        string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        HashSet<string> remaining = expectedProperties.ToHashSet(StringComparer.Ordinal);
        int propertyCount = 0;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            propertyCount++;
            if (!remaining.Remove(property.Name))
            {
                return false;
            }
        }

        return propertyCount == expectedProperties.Length && remaining.Count == 0;
    }

    private static bool IsAllowedCategory(
        string serviceCategory,
        IReadOnlyList<string> allowedCategories) =>
        serviceCategory.Equals(LeadAnalysisSchema.UnknownCategory, StringComparison.Ordinal) ||
        allowedCategories.Contains(serviceCategory, StringComparer.Ordinal);

    private static bool IsReasonCode(string value)
    {
        if (value[0] is < 'A' or > 'Z')
        {
            return false;
        }

        return value.Skip(1).All(
            static character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    }
}
