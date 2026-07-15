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
    int AttemptCount);

public sealed record AssignableUserResponse(
    Guid UserId,
    string DisplayName,
    string Role);

public sealed record LeadDetailResponse(
    LeadSummaryResponse Lead,
    IReadOnlyList<LeadTimelineItemResponse> Timeline,
    IReadOnlyList<PendingActionResponse> PendingActions,
    IReadOnlyList<AssignableUserResponse> AssignableUsers,
    IReadOnlyList<string> AllowedTransitions);

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

public sealed record ManualMessageRequest(
    string Body,
    string IdempotencyKey);

public sealed record AddLeadNoteRequest(string Body);
