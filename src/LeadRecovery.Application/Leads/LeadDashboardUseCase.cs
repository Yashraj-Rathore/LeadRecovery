using LeadRecovery.Domain.Analysis;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Leads;

public sealed class LeadDashboardUseCase(
    ILeadDashboardStore store,
    TimeProvider timeProvider)
{
    public Task<LeadDetail?> GetDetailAsync(
        Guid leadId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        cancellationToken.ThrowIfCancellationRequested();
        return store.GetDetailAsync(leadId, cancellationToken);
    }

    public Task<IReadOnlyList<AssignableUserItem>> ListAssignableUsersAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.ListAssignableUsersAsync(cancellationToken);
    }

    public Task<LeadOperationResult> AssignAsync(
        Guid leadId,
        Guid? assignedUserId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        RequireActor(actorUserId);
        if (assignedUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "An optional assigned user ID cannot be empty.",
                nameof(assignedUserId));
        }

        RequireVersion(expectedVersion);
        return store.AssignAsync(
            leadId,
            assignedUserId,
            expectedVersion,
            actorUserId,
            RequireCorrelationId(correlationId),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<LeadOperationResult> TransitionAsync(
        Guid leadId,
        LeadTransitionCommand command,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        RequireActor(actorUserId);
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.TargetStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        if (command.Reason?.Length > LeadFieldLimits.StaffOverrideReasonMaximumLength)
        {
            throw new ArgumentException(
                $"A transition reason cannot exceed " +
                $"{LeadFieldLimits.StaffOverrideReasonMaximumLength} characters.",
                nameof(command));
        }

        RequireVersion(expectedVersion);
        return store.TransitionAsync(
            leadId,
            command,
            expectedVersion,
            actorUserId,
            RequireCorrelationId(correlationId),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<LeadOperationResult> SetAutomationPausedAsync(
        Guid leadId,
        bool paused,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        RequireActor(actorUserId);
        RequireVersion(expectedVersion);
        return store.SetAutomationPausedAsync(
            leadId,
            paused,
            expectedVersion,
            actorUserId,
            RequireCorrelationId(correlationId),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<LeadOperationResult> AddNoteAsync(
        Guid leadId,
        string body,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        RequireActor(actorUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (body.Trim().Length > LeadNoteFieldLimits.BodyMaximumLength)
        {
            throw new ArgumentException(
                $"A lead note cannot exceed {LeadNoteFieldLimits.BodyMaximumLength} characters.",
                nameof(body));
        }

        return store.AddNoteAsync(
            leadId,
            body,
            actorUserId,
            RequireCorrelationId(correlationId),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<LeadOperationResult> QueueManualMessageAsync(
        Guid leadId,
        QueueManualMessageCommand command,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        RequireActor(actorUserId);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Body);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        if (command.Body.Length > MessageFieldLimits.BodyMaximumLength)
        {
            throw new ArgumentException(
                $"A message cannot exceed {MessageFieldLimits.BodyMaximumLength} characters.",
                nameof(command));
        }

        if (command.IdempotencyKey.Trim().Length >
            MessageFieldLimits.ClientIdempotencyKeyMaximumLength)
        {
            throw new ArgumentException(
                $"An idempotency key cannot exceed " +
                $"{MessageFieldLimits.ClientIdempotencyKeyMaximumLength} characters.",
                nameof(command));
        }

        return store.QueueManualMessageAsync(
            leadId,
            command,
            actorUserId,
            RequireCorrelationId(correlationId),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<LeadOperationResult> QueueBookingLinkAsync(
        Guid leadId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        RequireActor(actorUserId);
        RequireVersion(expectedVersion);
        return store.QueueBookingLinkAsync(
            leadId,
            expectedVersion,
            actorUserId,
            RequireCorrelationId(correlationId),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<LeadOperationResult> CancelScheduledActionAsync(
        Guid leadId,
        Guid actionId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        if (actionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty scheduled-action ID is required.",
                nameof(actionId));
        }

        RequireActor(actorUserId);
        return store.CancelScheduledActionAsync(
            leadId,
            actionId,
            actorUserId,
            RequireCorrelationId(correlationId),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<LeadOperationResult> ReviewAnalysisAsync(
        Guid leadId,
        Guid analysisId,
        ReviewLeadAnalysisCommand command,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireLeadId(leadId);
        if (analysisId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty AI analysis ID is required.",
                nameof(analysisId));
        }

        RequireActor(actorUserId);
        RequireVersion(expectedVersion);
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Action))
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        if (command.Action == LeadAnalysisReviewAction.Edit &&
            command.EditedValues is null)
        {
            throw new ArgumentException(
                "Edited values are required when editing a suggestion.",
                nameof(command));
        }

        if (command.Action != LeadAnalysisReviewAction.Edit &&
            command.EditedValues is not null)
        {
            throw new ArgumentException(
                "Edited values are allowed only for an edit review.",
                nameof(command));
        }

        if (command.CorrectionReason?.Trim().Length >
            AiAnalysisFieldLimits.CorrectionReasonMaximumLength)
        {
            throw new ArgumentException(
                $"A correction reason cannot exceed " +
                $"{AiAnalysisFieldLimits.CorrectionReasonMaximumLength} characters.",
                nameof(command));
        }

        return store.ReviewAnalysisAsync(
            leadId,
            analysisId,
            command,
            expectedVersion,
            actorUserId,
            RequireCorrelationId(correlationId),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static void RequireLeadId(Guid leadId)
    {
        if (leadId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty lead ID is required.", nameof(leadId));
        }
    }

    private static void RequireActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty actor user ID is required.",
                nameof(actorUserId));
        }
    }

    private static void RequireVersion(long expectedVersion)
    {
        if (expectedVersion < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedVersion),
                "The expected version cannot be negative.");
        }
    }

    private static string RequireCorrelationId(string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return correlationId.Trim();
    }
}
