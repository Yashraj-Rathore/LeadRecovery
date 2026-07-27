using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeadRecovery.Application.Analysis;

public static class LeadAnalysisScheduledActionTypes
{
    public const string AnalyzeLead = "AnalyzeLead";
}

public sealed record LeadAnalysisWorkflowOptions
{
    public const string DefaultCategoryQuestionKey = "service";

    public LeadAnalysisWorkflowOptions(
        bool enabled,
        string categoryQuestionKey = DefaultCategoryQuestionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryQuestionKey);
        string normalizedKey = categoryQuestionKey.Trim();
        if (normalizedKey.Length > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(categoryQuestionKey),
                "The AI category question key cannot exceed 100 characters.");
        }

        Enabled = enabled;
        CategoryQuestionKey = normalizedKey;
    }

    public bool Enabled { get; }

    public string CategoryQuestionKey { get; }
}

public sealed record LeadAnalysisScheduledActionPayload(
    int SchemaVersion,
    string AnalysisSchemaVersion,
    Guid SourceMessageId,
    Guid WorkflowDefinitionId,
    int WorkflowVersion,
    string CategoryQuestionKey,
    string[] AllowedCategories);

public static class LeadAnalysisScheduledActionPayloadSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize(LeadAnalysisScheduledActionPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    public static bool TryDeserialize(
        string json,
        out LeadAnalysisScheduledActionPayload? payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<LeadAnalysisScheduledActionPayload>(
                json,
                SerializerOptions);
            return payload is
            {
                SchemaVersion: 1,
                WorkflowVersion: > 0,
                CategoryQuestionKey.Length: > 0,
                AllowedCategories.Length: > 0,
            } &&
                payload.SourceMessageId != Guid.Empty &&
                payload.WorkflowDefinitionId != Guid.Empty &&
                payload.CategoryQuestionKey.Length <= 100 &&
                payload.AllowedCategories.Length <= LeadAnalysisSchema.MaximumAllowedCategories &&
                payload.AllowedCategories.All(category =>
                    !string.IsNullOrWhiteSpace(category) &&
                    category.Length <= LeadAnalysisSchema.MaximumCategoryLength &&
                    !category.Equals(
                        LeadAnalysisSchema.UnknownCategory,
                        StringComparison.OrdinalIgnoreCase)) &&
                payload.AllowedCategories.Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                    payload.AllowedCategories.Length &&
                string.Equals(
                    payload.AnalysisSchemaVersion,
                    LeadAnalysisSchema.CurrentVersion,
                    StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            payload = null;
            return false;
        }
    }
}

public sealed record PreparedLeadAnalysis(
    Guid TenantId,
    Guid ActionId,
    Guid LeadId,
    string InputHash,
    LeadAnalysisRequest Request);

public enum LeadAnalysisWorkflowOutcome
{
    Ignored,
    Persisted,
    PersistedNeedsHuman,
    FallbackRecorded,
    FallbackNeedsHuman,
}

public interface ILeadAnalysisWorkflowPersistence
{
    Task<PreparedLeadAnalysis?> PrepareAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<LeadAnalysisWorkflowOutcome> CompleteAsync(
        PreparedLeadAnalysis prepared,
        LeadAnalysisResult result,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
