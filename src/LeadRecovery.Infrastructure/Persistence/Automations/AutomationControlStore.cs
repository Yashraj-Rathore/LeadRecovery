using System.Data;
using System.Text.Json;

using LeadRecovery.Application.Analysis;
using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Observability;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeadRecovery.Infrastructure.Persistence.Automations;

internal sealed class AutomationControlStore(
    LeadRecoveryDbContext dbContext,
    ITenantContext tenantContext)
    : IAutomationControlStore
{
    public async Task<TenantAutomationSnapshot> GetTenantAsync(
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;
        return await dbContext.Tenants
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => new TenantAutomationSnapshot(
                tenant.AutomationEnabled,
                tenant.Version))
            .SingleAsync(cancellationToken);
    }

    public async Task<TenantAutomationMutation> SetTenantAsync(
        bool enabled,
        long expectedVersion,
        Guid actorUserId,
        AutomationControlReason reason,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        Tenant tenant = await dbContext.Tenants
            .FromSqlInterpolated(
                $"select * from tenants where id = {tenantId} for update")
            .SingleAsync(cancellationToken);
        if (tenant.Version != expectedVersion)
        {
            await transaction.CommitAsync(cancellationToken);
            return new TenantAutomationMutation(
                AutomationUpdateOutcome.Conflict,
                new TenantAutomationSnapshot(tenant.AutomationEnabled, tenant.Version),
                0);
        }

        bool previouslyEnabled = tenant.AutomationEnabled;
        bool changed = previouslyEnabled != enabled;
        if (changed)
        {
            tenant.SetAutomationEnabled(enabled, now);
        }

        int cancelledActionCount = enabled
            ? 0
            : await CancelPendingForTenantAsync(tenantId, now, cancellationToken);
        if (changed || cancelledActionCount > 0)
        {
            dbContext.AuditEvents.Add(new AuditEvent(
                Guid.CreateVersion7(),
                tenantId,
                "User",
                actorUserId.ToString(),
                enabled ? "TenantAutomationEnabled" : "TenantAutomationDisabled",
                nameof(Tenant),
                tenantId.ToString("N"),
                correlationId,
                now,
                JsonSerializer.Serialize(new { enabled = previouslyEnabled }),
                JsonSerializer.Serialize(new
                {
                    enabled,
                    reasonCode = reason.ToString(),
                    cancelledActionCount,
                })));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (cancelledActionCount > 0)
        {
            LeadRecoveryTelemetry.RecordAutomationCancellation(
                "tenant",
                tenantId,
                cancelledActionCount);
        }

        return new TenantAutomationMutation(
            changed ? AutomationUpdateOutcome.Updated : AutomationUpdateOutcome.Unchanged,
            new TenantAutomationSnapshot(tenant.AutomationEnabled, tenant.Version),
            cancelledActionCount);
    }

    public async Task<int> CancelAllPendingAutomatedActionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        var pendingByTenant = await PendingAutomatedActions(
                dbContext.ScheduledActions.IgnoreQueryFilters())
            .GroupBy(action => action.TenantId)
            .Select(group => new
            {
                TenantId = group.Key,
                Count = group.Count(),
            })
            .ToArrayAsync(cancellationToken);
        int cancelled = await PendingAutomatedActions(
                dbContext.ScheduledActions.IgnoreQueryFilters())
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        action => action.Status,
                        ScheduledActionStatus.Cancelled)
                    .SetProperty(action => action.UpdatedAtUtc, now),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var tenant in pendingByTenant)
        {
            LeadRecoveryTelemetry.RecordAutomationCancellation(
                "global",
                tenant.TenantId,
                tenant.Count);
        }

        return cancelled;
    }

    private async Task<int> CancelPendingForTenantAsync(
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<ScheduledAction> pending = await PendingAutomatedActions(
                dbContext.ScheduledActions.Where(action => action.TenantId == tenantId))
            .ToListAsync(cancellationToken);
        foreach (ScheduledAction action in pending)
        {
            action.Cancel(now);
        }

        return pending.Count;
    }

    private static IQueryable<ScheduledAction> PendingAutomatedActions(
        IQueryable<ScheduledAction> actions) =>
        actions.Where(action =>
            action.Status == ScheduledActionStatus.Pending &&
            (action.ActionType == ProcessCallStatusWebhookUseCase.RecoveryActionType ||
                action.ActionType ==
                    WorkflowScheduledActionTypes.SendQualificationQuestion ||
                action.ActionType == WorkflowScheduledActionTypes.SendBookingLink ||
                action.ActionType == WorkflowScheduledActionTypes.SendFollowUpSms ||
                action.ActionType == LeadAnalysisScheduledActionTypes.AnalyzeLead));
}
