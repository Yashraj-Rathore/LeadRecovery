using LeadRecovery.Domain.Analysis;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Leads;

public enum LeadAssignmentFilter
{
    All,
    Unassigned,
    Mine,
}

public sealed record LeadInboxCriteria(
    LeadStatus? Status,
    LeadUrgency? Urgency,
    LeadAssignmentFilter Assignment,
    Guid CurrentUserId,
    Guid? AssignedUserId = null);

public sealed record LeadTimelineItem(
    Guid Id,
    string Type,
    string Label,
    string? Body,
    string? Direction,
    string? Kind,
    string? Status,
    string? FailureDescription,
    string? ActorName,
    DateTimeOffset OccurredAtUtc);

public sealed record PendingActionItem(
    Guid Id,
    string ActionType,
    ScheduledActionStatus Status,
    DateTimeOffset ScheduledForUtc,
    int AttemptCount,
    bool IsCancellable);

public sealed record QualificationAnswerItem(
    Guid Id,
    string QuestionKey,
    string QuestionPrompt,
    string? Value,
    string Outcome,
    DateTimeOffset CreatedAtUtc);

public sealed record AssignableUserItem(
    Guid UserId,
    string DisplayName,
    string Role);

public sealed record AiAnalysisReviewItem(
    Guid Id,
    string SchemaVersion,
    IReadOnlyList<string> AllowedCategories,
    AiAnalysisValues Suggestion,
    double Confidence,
    bool RequiresHumanReview,
    IReadOnlyList<string> ReasonCodes,
    AiAnalysisReviewStatus ReviewStatus,
    AiAnalysisValues? ReviewedValues,
    string? CorrectionReason,
    Guid? ReviewedByUserId,
    string? ReviewedByUserName,
    DateTimeOffset? ReviewedAtUtc,
    long Version,
    DateTimeOffset CreatedAtUtc);

public sealed record LeadDetail(
    LeadInboxItem Lead,
    IReadOnlyList<LeadTimelineItem> Timeline,
    IReadOnlyList<PendingActionItem> PendingActions,
    IReadOnlyList<AssignableUserItem> AssignableUsers,
    IReadOnlyList<LeadStatus> AllowedTransitions,
    IReadOnlyList<QualificationAnswerItem> QualificationAnswers,
    string? CurrentQualificationQuestion,
    string? BookingUrl,
    IReadOnlyList<AiAnalysisReviewItem> AiAnalyses);

public enum LeadAnalysisReviewAction
{
    Accept,
    Edit,
    Reject,
}

public sealed record ReviewLeadAnalysisCommand(
    LeadAnalysisReviewAction Action,
    AiAnalysisValues? EditedValues,
    string? CorrectionReason);

public enum LeadOperationStatus
{
    Success,
    NotFound,
    Conflict,
    Invalid,
    PolicyBlocked,
}

public sealed record LeadOperationResult(
    LeadOperationStatus Status,
    string? Detail = null,
    Guid? ResourceId = null)
{
    public static LeadOperationResult Success(Guid? resourceId = null) =>
        new(LeadOperationStatus.Success, ResourceId: resourceId);

    public static LeadOperationResult NotFound() =>
        new(LeadOperationStatus.NotFound);

    public static LeadOperationResult Conflict() =>
        new(LeadOperationStatus.Conflict);

    public static LeadOperationResult Invalid(string detail) =>
        new(LeadOperationStatus.Invalid, detail);

    public static LeadOperationResult PolicyBlocked(string detail) =>
        new(LeadOperationStatus.PolicyBlocked, detail);
}

public sealed record LeadTransitionCommand(
    LeadStatus TargetStatus,
    string? Reason,
    LeadCloseReason? CloseReason,
    bool MinimumRequiredDetailsPresent);

public sealed record QueueManualMessageCommand(
    string Body,
    string IdempotencyKey);
