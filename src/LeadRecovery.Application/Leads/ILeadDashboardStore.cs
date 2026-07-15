namespace LeadRecovery.Application.Leads;

public interface ILeadDashboardStore
{
    Task<LeadDetail?> GetDetailAsync(
        Guid leadId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignableUserItem>> ListAssignableUsersAsync(
        CancellationToken cancellationToken);

    Task<LeadOperationResult> AssignAsync(
        Guid leadId,
        Guid? assignedUserId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<LeadOperationResult> TransitionAsync(
        Guid leadId,
        LeadTransitionCommand command,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<LeadOperationResult> SetAutomationPausedAsync(
        Guid leadId,
        bool paused,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<LeadOperationResult> AddNoteAsync(
        Guid leadId,
        string body,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<LeadOperationResult> QueueManualMessageAsync(
        Guid leadId,
        QueueManualMessageCommand command,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
