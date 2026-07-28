using LeadRecovery.Application.Tenancy;

namespace LeadRecovery.Application.Retention;

public enum RetentionExecutionMode
{
    DryRun = 0,
    Delete = 1,
}

public sealed record RetentionRuntimeOptions
{
    public const int BatchSizeDefault = 100;
    public const int BatchSizeMaximum = 1_000;

    public RetentionRuntimeOptions(
        bool enabled,
        RetentionExecutionMode mode,
        int batchSize,
        bool backupConfirmed)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (batchSize is < 1 or > BatchSizeMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                $"Retention batch size must be between 1 and {BatchSizeMaximum}.");
        }

        if (enabled && mode == RetentionExecutionMode.Delete && !backupConfirmed)
        {
            throw new InvalidOperationException(
                "Destructive retention requires an explicit backup confirmation.");
        }

        Enabled = enabled;
        Mode = mode;
        BatchSize = batchSize;
        BackupConfirmed = backupConfirmed;
    }

    public bool Enabled { get; }

    public RetentionExecutionMode Mode { get; }

    public int BatchSize { get; }

    public bool BackupConfirmed { get; }
}

public sealed record TenantRetentionPolicySnapshot(Guid TenantId, int RetentionDays);

public sealed record TenantRetentionResult(
    Guid TenantId,
    RetentionExecutionMode Mode,
    DateTimeOffset CutoffUtc,
    int CandidateLeadCount,
    int DeletedLeadCount,
    Guid AuditEventId);

public interface IRetentionStore
{
    Task<IReadOnlyList<TenantRetentionPolicySnapshot>> ListEnabledPoliciesAsync(
        CancellationToken cancellationToken);

    Task<TenantRetentionResult> ProcessTenantAsync(
        TenantRetentionPolicySnapshot policy,
        RetentionExecutionMode mode,
        DateTimeOffset cutoffUtc,
        int batchSize,
        Guid runId,
        CancellationToken cancellationToken);
}

public sealed class RetentionUseCase(
    IRetentionStore store,
    ITenantExecutionScope tenantExecutionScope,
    RetentionRuntimeOptions options,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<TenantRetentionResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return [];
        }

        IReadOnlyList<TenantRetentionPolicySnapshot> policies =
            await store.ListEnabledPoliciesAsync(cancellationToken);
        List<TenantRetentionResult> results = new(policies.Count);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid runId = Guid.CreateVersion7();

        foreach (TenantRetentionPolicySnapshot policy in policies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IDisposable tenantScope = tenantExecutionScope.Begin(policy.TenantId);
            DateTimeOffset cutoffUtc = now.AddDays(-policy.RetentionDays);
            results.Add(await store.ProcessTenantAsync(
                policy,
                options.Mode,
                cutoffUtc,
                options.BatchSize,
                runId,
                cancellationToken));
        }

        return results;
    }
}
