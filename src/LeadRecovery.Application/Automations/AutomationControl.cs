namespace LeadRecovery.Application.Automations;

public interface IAutomationRuntimePolicy
{
    bool GlobalAutomationEnabled { get; }
}

public sealed record AutomationRuntimeOptions(bool GlobalAutomationEnabled)
    : IAutomationRuntimePolicy;

public enum AutomationControlReason
{
    TenantRequest,
    OperationalIncident,
    PlannedMaintenance,
    IncidentResolved,
    MaintenanceComplete,
}

public enum AutomationUpdateOutcome
{
    Updated,
    Unchanged,
    Conflict,
}

public sealed record TenantAutomationSnapshot(
    bool Enabled,
    long Version);

public sealed record TenantAutomationMutation(
    AutomationUpdateOutcome Outcome,
    TenantAutomationSnapshot Snapshot,
    int CancelledActionCount);

public sealed record AutomationStatus(
    bool GlobalEnabled,
    bool TenantEnabled,
    bool EffectiveEnabled,
    long TenantVersion);

public sealed record AutomationUpdateResult(
    AutomationUpdateOutcome Outcome,
    AutomationStatus Status,
    int CancelledActionCount);

public sealed record GlobalAutomationEnforcementResult(
    bool GlobalEnabled,
    int CancelledActionCount);

public interface IAutomationControlStore
{
    Task<TenantAutomationSnapshot> GetTenantAsync(
        CancellationToken cancellationToken);

    Task<TenantAutomationMutation> SetTenantAsync(
        bool enabled,
        long expectedVersion,
        Guid actorUserId,
        AutomationControlReason reason,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> CancelAllPendingAutomatedActionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class AutomationControlUseCase(
    IAutomationControlStore store,
    IAutomationRuntimePolicy runtimePolicy,
    TimeProvider timeProvider)
{
    public async Task<AutomationStatus> GetAsync(CancellationToken cancellationToken)
    {
        TenantAutomationSnapshot tenant = await store.GetTenantAsync(cancellationToken);
        return CreateStatus(tenant);
    }

    public async Task<AutomationUpdateResult> SetTenantAsync(
        bool enabled,
        long expectedVersion,
        Guid actorUserId,
        AutomationControlReason reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor user ID is required.", nameof(actorUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ValidateReason(enabled, reason);
        TenantAutomationMutation mutation = await store.SetTenantAsync(
            enabled,
            expectedVersion,
            actorUserId,
            reason,
            correlationId.Trim(),
            timeProvider.GetUtcNow(),
            cancellationToken);
        return new AutomationUpdateResult(
            mutation.Outcome,
            CreateStatus(mutation.Snapshot),
            mutation.CancelledActionCount);
    }

    public async Task<GlobalAutomationEnforcementResult> EnforceGlobalDisableAsync(
        CancellationToken cancellationToken)
    {
        if (runtimePolicy.GlobalAutomationEnabled)
        {
            return new GlobalAutomationEnforcementResult(true, 0);
        }

        int cancelled = await store.CancelAllPendingAutomatedActionsAsync(
            timeProvider.GetUtcNow(),
            cancellationToken);
        return new GlobalAutomationEnforcementResult(false, cancelled);
    }

    private AutomationStatus CreateStatus(TenantAutomationSnapshot tenant) =>
        new(
            runtimePolicy.GlobalAutomationEnabled,
            tenant.Enabled,
            runtimePolicy.GlobalAutomationEnabled && tenant.Enabled,
            tenant.Version);

    private static void ValidateReason(bool enabled, AutomationControlReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        bool valid = enabled
            ? reason is AutomationControlReason.TenantRequest or
                AutomationControlReason.IncidentResolved or
                AutomationControlReason.MaintenanceComplete
            : reason is AutomationControlReason.TenantRequest or
                AutomationControlReason.OperationalIncident or
                AutomationControlReason.PlannedMaintenance;
        if (!valid)
        {
            throw new ArgumentException(
                "The reason code is not valid for the requested automation state.",
                nameof(reason));
        }
    }
}
