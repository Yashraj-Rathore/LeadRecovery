using LeadRecovery.Application.Leads;

namespace LeadRecovery.Application.Tests;

public sealed class LeadDashboardUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ManualMessageValidatesLimitsAndDelegatesServerActorContext()
    {
        StubDashboardStore store = new();
        LeadDashboardUseCase useCase = new(store, new FixedTimeProvider(Now));
        Guid leadId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        QueueManualMessageCommand command = new("Please call us back.", "ui-request-1");

        LeadOperationResult result = await useCase.QueueManualMessageAsync(
            leadId,
            command,
            actorId,
            "correlation-1",
            TestContext.Current.CancellationToken);

        Assert.Equal(LeadOperationStatus.Success, result.Status);
        Assert.Equal(leadId, store.LeadId);
        Assert.Equal(actorId, store.ActorUserId);
        Assert.Same(command, store.ManualMessageCommand);
        Assert.Equal(Now, store.Now);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.QueueManualMessageAsync(
                leadId,
                new QueueManualMessageCommand(" ", "key"),
                actorId,
                "correlation-1",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrencyMutationsRejectInvalidVersionsBeforePersistence()
    {
        StubDashboardStore store = new();
        LeadDashboardUseCase useCase = new(store, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            useCase.SetAutomationPausedAsync(
                Guid.CreateVersion7(),
                paused: true,
                expectedVersion: -1,
                Guid.CreateVersion7(),
                "correlation-2",
                TestContext.Current.CancellationToken));

        Assert.Null(store.LeadId);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubDashboardStore : ILeadDashboardStore
    {
        public Guid? LeadId { get; private set; }

        public Guid? ActorUserId { get; private set; }

        public QueueManualMessageCommand? ManualMessageCommand { get; private set; }

        public DateTimeOffset? Now { get; private set; }

        public Task<LeadDetail?> GetDetailAsync(
            Guid leadId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LeadDetail?>(null);

        public Task<IReadOnlyList<AssignableUserItem>> ListAssignableUsersAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignableUserItem>>([]);

        public Task<LeadOperationResult> AssignAsync(
            Guid leadId,
            Guid? assignedUserId,
            long expectedVersion,
            Guid actorUserId,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(LeadOperationResult.Success());

        public Task<LeadOperationResult> TransitionAsync(
            Guid leadId,
            LeadTransitionCommand command,
            long expectedVersion,
            Guid actorUserId,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(LeadOperationResult.Success());

        public Task<LeadOperationResult> SetAutomationPausedAsync(
            Guid leadId,
            bool paused,
            long expectedVersion,
            Guid actorUserId,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(LeadOperationResult.Success());

        public Task<LeadOperationResult> AddNoteAsync(
            Guid leadId,
            string body,
            Guid actorUserId,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(LeadOperationResult.Success());

        public Task<LeadOperationResult> QueueManualMessageAsync(
            Guid leadId,
            QueueManualMessageCommand command,
            Guid actorUserId,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LeadId = leadId;
            ActorUserId = actorUserId;
            ManualMessageCommand = command;
            Now = now;
            return Task.FromResult(LeadOperationResult.Success());
        }
    }
}
