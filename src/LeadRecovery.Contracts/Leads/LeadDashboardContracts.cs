namespace LeadRecovery.Contracts.Leads;

public sealed record LeadTimelineItemResponse(
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

public sealed record PendingActionResponse(
    Guid Id,
    string ActionType,
    string Status,
    DateTimeOffset ScheduledForUtc,
    int AttemptCount,
    bool IsCancellable);

public sealed record QualificationAnswerResponse(
    Guid Id,
    string QuestionKey,
    string QuestionPrompt,
    string? Value,
    string Outcome,
    DateTimeOffset CreatedAtUtc);

public sealed record AssignableUserResponse(
    Guid UserId,
    string DisplayName,
    string Role);

public sealed record AiAnalysisValuesResponse(
    string ServiceCategory,
    string Urgency,
    string Summary,
    string? City,
    string? PostalCode,
    string? PreferredCallbackWindow,
    string? SuggestedReply);

public sealed record AiAnalysisReviewResponse(
    Guid Id,
    string SchemaVersion,
    IReadOnlyList<string> AllowedCategories,
    AiAnalysisValuesResponse Suggestion,
    double Confidence,
    bool RequiresHumanReview,
    IReadOnlyList<string> ReasonCodes,
    string ReviewStatus,
    AiAnalysisValuesResponse? ReviewedValues,
    string? CorrectionReason,
    Guid? ReviewedByUserId,
    string? ReviewedByUserName,
    DateTimeOffset? ReviewedAtUtc,
    string RowVersion,
    DateTimeOffset CreatedAtUtc);

public sealed record LeadDetailResponse(
    LeadSummaryResponse Lead,
    IReadOnlyList<LeadTimelineItemResponse> Timeline,
    IReadOnlyList<PendingActionResponse> PendingActions,
    IReadOnlyList<AssignableUserResponse> AssignableUsers,
    IReadOnlyList<string> AllowedTransitions,
    IReadOnlyList<QualificationAnswerResponse> QualificationAnswers,
    string? CurrentQualificationQuestion,
    string? BookingUrl,
    IReadOnlyList<AiAnalysisReviewResponse> AiAnalyses);

public sealed record AssignLeadRequest(
    Guid? AssignedUserId,
    string ExpectedRowVersion);

public sealed record TransitionLeadRequest(
    string TargetStatus,
    string? Reason,
    string? CloseReason,
    bool MinimumRequiredDetailsPresent,
    string ExpectedRowVersion);

public sealed record LeadAutomationRequest(string ExpectedRowVersion);

public sealed record LeadBookingRequest(string ExpectedRowVersion);

public sealed record ManualMessageRequest(
    string Body,
    string IdempotencyKey);

public sealed record AddLeadNoteRequest(string Body);

public sealed record AcceptAiAnalysisRequest(
    string ExpectedRowVersion,
    string? CorrectionReason);

public sealed record EditAiAnalysisRequest(
    string ServiceCategory,
    string Urgency,
    string Summary,
    string? City,
    string? PostalCode,
    string? PreferredCallbackWindow,
    string? SuggestedReply,
    string? CorrectionReason,
    string ExpectedRowVersion);

public sealed record RejectAiAnalysisRequest(
    string ExpectedRowVersion,
    string? CorrectionReason);
