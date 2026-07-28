using LeadRecovery.Application.Automations;

namespace LeadRecovery.Application.Tests;

public sealed class AutomationControlUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task StatusRequiresBothGlobalAndTenantEnablement(
        bool globalEnabled,
        bool tenantEnabled,
        bool effectiveEnabled)
    {
        RecordingStore store = new()
        {
            Snapshot = new TenantAutomationSnapshot(tenantEnabled, 4),
        };
        AutomationControlUseCase useCase = CreateUseCase(store, globalEnabled);

        AutomationStatus status = await useCase.GetAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(globalEnabled, status.GlobalEnabled);
        Assert.Equal(tenantEnabled, status.TenantEnabled);
        Assert.Equal(effectiveEnabled, status.EffectiveEnabled);
        Assert.Equal(4, status.TenantVersion);
    }

    [Fact]
    public async Task TenantDisableUsesFixedReasonAndReturnsCancellationCount()
    {
        Guid actorId = Guid.CreateVersion7();
        RecordingStore store = new()
        {
            Mutation = new TenantAutomationMutation(
                AutomationUpdateOutcome.Updated,
                new TenantAutomationSnapshot(false, 8),
                3),
        };
        AutomationControlUseCase useCase = CreateUseCase(store, globalEnabled: true);

        AutomationUpdateResult result = await useCase.SetTenantAsync(
            enabled: false,
            expectedVersion: 7,
            actorId,
            AutomationControlReason.OperationalIncident,
            " correlation-123 ",
            TestContext.Current.CancellationToken);

        Assert.Equal(AutomationUpdateOutcome.Updated, result.Outcome);
        Assert.False(result.Status.EffectiveEnabled);
        Assert.Equal(3, result.CancelledActionCount);
        Assert.Equal(actorId, store.ActorUserId);
        Assert.Equal(AutomationControlReason.OperationalIncident, store.Reason);
        Assert.Equal("correlation-123", store.CorrelationId);
        Assert.Equal(Now, store.Now);
    }

    [Fact]
    public async Task DirectionSpecificReasonIsValidatedBeforePersistence()
    {
        RecordingStore store = new();
        AutomationControlUseCase useCase = CreateUseCase(store, globalEnabled: true);

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.SetTenantAsync(
            enabled: true,
            expectedVersion: 0,
            Guid.CreateVersion7(),
            AutomationControlReason.OperationalIncident,
            "correlation",
            TestContext.Current.CancellationToken));

        Assert.Equal(0, store.SetCallCount);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 5)]
    public async Task GlobalEnforcementCancelsOnlyWhenSwitchIsOff(
        bool globalEnabled,
        int expectedCancelled)
    {
        RecordingStore store = new() { GlobalCancellationResult = 5 };
        AutomationControlUseCase useCase = CreateUseCase(store, globalEnabled);

        GlobalAutomationEnforcementResult result =
            await useCase.EnforceGlobalDisableAsync(TestContext.Current.CancellationToken);

        Assert.Equal(globalEnabled, result.GlobalEnabled);
        Assert.Equal(expectedCancelled, result.CancelledActionCount);
        Assert.Equal(globalEnabled ? 0 : 1, store.GlobalCancellationCallCount);
    }

    private static AutomationControlUseCase CreateUseCase(
        RecordingStore store,
        bool globalEnabled) =>
        new(
            store,
            new AutomationRuntimeOptions(globalEnabled),
            new FixedTimeProvider(Now));

    private sealed class RecordingStore : IAutomationControlStore
    {
        public TenantAutomationSnapshot Snapshot { get; init; } = new(false, 0);

        public TenantAutomationMutation Mutation { get; init; } =
            new(AutomationUpdateOutcome.Unchanged, new(false, 0), 0);

        public int GlobalCancellationResult { get; init; }

        public int SetCallCount { get; private set; }

        public int GlobalCancellationCallCount { get; private set; }

        public Guid ActorUserId { get; private set; }

        public AutomationControlReason Reason { get; private set; }

        public string? CorrelationId { get; private set; }

        public DateTimeOffset Now { get; private set; }

        public Task<TenantAutomationSnapshot> GetTenantAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task<TenantAutomationMutation> SetTenantAsync(
            bool enabled,
            long expectedVersion,
            Guid actorUserId,
            AutomationControlReason reason,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            SetCallCount++;
            ActorUserId = actorUserId;
            Reason = reason;
            CorrelationId = correlationId;
            Now = now;
            return Task.FromResult(Mutation);
        }

        public Task<int> CancelAllPendingAutomatedActionsAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            GlobalCancellationCallCount++;
            Now = now;
            return Task.FromResult(GlobalCancellationResult);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
