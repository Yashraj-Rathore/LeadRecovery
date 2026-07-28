using LeadRecovery.Application.Retention;
using LeadRecovery.Application.Tenancy;

namespace LeadRecovery.Application.Tests;

public sealed class RetentionUseCaseTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 7, 28, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DryRunAppliesEachTenantPolicyInsideMatchingScope()
    {
        Guid alpha = Guid.CreateVersion7();
        Guid beta = Guid.CreateVersion7();
        FakeTenantExecutionScope executionScope = new();
        FakeRetentionStore store = new(
            executionScope,
            [
                new TenantRetentionPolicySnapshot(alpha, 30),
                new TenantRetentionPolicySnapshot(beta, 365),
            ]);
        RetentionUseCase useCase = new(
            store,
            executionScope,
            new RetentionRuntimeOptions(
                enabled: true,
                RetentionExecutionMode.DryRun,
                batchSize: 50,
                backupConfirmed: false),
            new FixedTimeProvider(NowUtc));

        IReadOnlyList<TenantRetentionResult> results = await useCase.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Collection(
            store.Calls,
            call =>
            {
                Assert.Equal(alpha, call.Policy.TenantId);
                Assert.Equal(alpha, call.ActiveTenantId);
                Assert.Equal(NowUtc.AddDays(-30), call.CutoffUtc);
                Assert.Equal(RetentionExecutionMode.DryRun, call.Mode);
                Assert.Equal(50, call.BatchSize);
            },
            call =>
            {
                Assert.Equal(beta, call.Policy.TenantId);
                Assert.Equal(beta, call.ActiveTenantId);
                Assert.Equal(NowUtc.AddDays(-365), call.CutoffUtc);
            });
        Assert.Null(executionScope.ActiveTenantId);
    }

    [Fact]
    public async Task DisabledRetentionDoesNotReadPolicies()
    {
        FakeTenantExecutionScope executionScope = new();
        FakeRetentionStore store = new(executionScope, []);
        RetentionUseCase useCase = new(
            store,
            executionScope,
            new RetentionRuntimeOptions(
                enabled: false,
                RetentionExecutionMode.DryRun,
                RetentionRuntimeOptions.BatchSizeDefault,
                backupConfirmed: false),
            new FixedTimeProvider(NowUtc));

        IReadOnlyList<TenantRetentionResult> results = await useCase.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Empty(results);
        Assert.Equal(0, store.ListCallCount);
    }

    [Fact]
    public void DeleteModeRequiresBackupConfirmation()
    {
        Assert.Throws<InvalidOperationException>(() => new RetentionRuntimeOptions(
            enabled: true,
            RetentionExecutionMode.Delete,
            RetentionRuntimeOptions.BatchSizeDefault,
            backupConfirmed: false));
    }

    private sealed class FakeRetentionStore(
        FakeTenantExecutionScope executionScope,
        IReadOnlyList<TenantRetentionPolicySnapshot> policies) : IRetentionStore
    {
        public int ListCallCount { get; private set; }

        public List<ProcessCall> Calls { get; } = [];

        public Task<IReadOnlyList<TenantRetentionPolicySnapshot>> ListEnabledPoliciesAsync(
            CancellationToken cancellationToken)
        {
            ListCallCount++;
            return Task.FromResult(policies);
        }

        public Task<TenantRetentionResult> ProcessTenantAsync(
            TenantRetentionPolicySnapshot policy,
            RetentionExecutionMode mode,
            DateTimeOffset cutoffUtc,
            int batchSize,
            Guid runId,
            CancellationToken cancellationToken)
        {
            Guid activeTenantId = Assert.IsType<Guid>(executionScope.ActiveTenantId);
            Calls.Add(new ProcessCall(
                policy,
                activeTenantId,
                mode,
                cutoffUtc,
                batchSize));
            return Task.FromResult(new TenantRetentionResult(
                policy.TenantId,
                mode,
                cutoffUtc,
                0,
                0,
                Guid.CreateVersion7()));
        }
    }

    private sealed class FakeTenantExecutionScope : ITenantExecutionScope
    {
        public Guid? ActiveTenantId { get; private set; }

        public IDisposable Begin(Guid tenantId)
        {
            Assert.Null(ActiveTenantId);
            ActiveTenantId = tenantId;
            return new CallbackDisposable(() => ActiveTenantId = null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }

    private sealed record ProcessCall(
        TenantRetentionPolicySnapshot Policy,
        Guid ActiveTenantId,
        RetentionExecutionMode Mode,
        DateTimeOffset CutoffUtc,
        int BatchSize);
}
