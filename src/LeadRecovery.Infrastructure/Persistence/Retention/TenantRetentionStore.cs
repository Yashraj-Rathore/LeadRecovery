using System.Data;
using System.Text.Json;

using LeadRecovery.Application.Retention;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Leads;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeadRecovery.Infrastructure.Persistence.Retention;

internal sealed class TenantRetentionStore(
    LeadRecoveryDbContext dbContext,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IRetentionStore
{
    public async Task<IReadOnlyList<TenantRetentionPolicySnapshot>>
        ListEnabledPoliciesAsync(CancellationToken cancellationToken) =>
        await dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.DataRetentionEnabled)
            .OrderBy(tenant => tenant.Id)
            .Select(tenant => new TenantRetentionPolicySnapshot(
                tenant.Id,
                tenant.DataRetentionDays))
            .ToListAsync(cancellationToken);

    public async Task<TenantRetentionResult> ProcessTenantAsync(
        TenantRetentionPolicySnapshot policy,
        RetentionExecutionMode mode,
        DateTimeOffset cutoffUtc,
        int batchSize,
        Guid runId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (tenantContext.TenantId != policy.TenantId)
        {
            throw new InvalidOperationException(
                "Retention can run only inside the policy tenant's execution scope.");
        }

        if (cutoffUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The retention cutoff must be UTC.", nameof(cutoffUtc));
        }

        if (batchSize is < 1 or > RetentionRuntimeOptions.BatchSizeMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A retention run ID is required.", nameof(runId));
        }

        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
        TenantRetentionPolicySnapshot? persistedPolicy = await dbContext.Tenants
            .AsNoTracking()
            .Where(tenant =>
                tenant.Id == policy.TenantId &&
                tenant.DataRetentionEnabled)
            .Select(tenant => new TenantRetentionPolicySnapshot(
                tenant.Id,
                tenant.DataRetentionDays))
            .SingleOrDefaultAsync(cancellationToken);
        if (persistedPolicy is null || persistedPolicy != policy)
        {
            throw new InvalidOperationException(
                "The tenant retention policy changed before execution; retry with a fresh policy.");
        }

        Guid[] leadIds = await dbContext.Leads
            .AsNoTracking()
            .Where(lead =>
                lead.TenantId == policy.TenantId &&
                (lead.Status == LeadStatus.Closed || lead.Status == LeadStatus.ClosedWon) &&
                lead.ClosedAtUtc != null &&
                lead.ClosedAtUtc < cutoffUtc)
            .OrderBy(lead => lead.ClosedAtUtc)
            .ThenBy(lead => lead.Id)
            .Select(lead => lead.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        RetentionCounts counts = await CountDependentsAsync(
            policy.TenantId,
            leadIds,
            cancellationToken);
        Guid auditEventId = Guid.CreateVersion7();

        int deletedLeadCount = 0;
        if (mode == RetentionExecutionMode.Delete && leadIds.Length > 0)
        {
            _ = await dbContext.QualificationAnswers
                .Where(answer =>
                    answer.TenantId == policy.TenantId &&
                    leadIds.Contains(answer.LeadId))
                .ExecuteDeleteAsync(cancellationToken);
            _ = await dbContext.AiAnalyses
                .Where(analysis =>
                    analysis.TenantId == policy.TenantId &&
                    leadIds.Contains(analysis.LeadId))
                .ExecuteDeleteAsync(cancellationToken);
            _ = await dbContext.LeadNotes
                .Where(note =>
                    note.TenantId == policy.TenantId &&
                    leadIds.Contains(note.LeadId))
                .ExecuteDeleteAsync(cancellationToken);
            _ = await dbContext.ScheduledActions
                .Where(action =>
                    action.TenantId == policy.TenantId &&
                    leadIds.Contains(action.LeadId))
                .ExecuteDeleteAsync(cancellationToken);
            _ = await dbContext.Messages
                .Where(message =>
                    message.TenantId == policy.TenantId &&
                    leadIds.Contains(message.LeadId))
                .ExecuteDeleteAsync(cancellationToken);
            _ = await dbContext.Conversations
                .Where(conversation =>
                    conversation.TenantId == policy.TenantId &&
                    leadIds.Contains(conversation.LeadId))
                .ExecuteDeleteAsync(cancellationToken);
            deletedLeadCount = await dbContext.Leads
                .Where(lead =>
                    lead.TenantId == policy.TenantId &&
                    leadIds.Contains(lead.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        string manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            mode = mode.ToString(),
            retentionDays = policy.RetentionDays,
            cutoffUtc,
            batchSize,
            candidateLeadCount = leadIds.Length,
            deletedLeadCount,
            dependentCounts = counts,
            containsPersonalData = false,
        });
        dbContext.AuditEvents.Add(new AuditEvent(
            auditEventId,
            policy.TenantId,
            "System",
            actorId: null,
            mode == RetentionExecutionMode.DryRun
                ? "Retention.DryRun"
                : "Retention.Deleted",
            "TenantRetention",
            policy.TenantId.ToString("N"),
            runId.ToString("N"),
            timeProvider.GetUtcNow(),
            afterJson: manifestJson));
        _ = await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new TenantRetentionResult(
            policy.TenantId,
            mode,
            cutoffUtc,
            leadIds.Length,
            deletedLeadCount,
            auditEventId);
    }

    private async Task<RetentionCounts> CountDependentsAsync(
        Guid tenantId,
        Guid[] leadIds,
        CancellationToken cancellationToken)
    {
        if (leadIds.Length == 0)
        {
            return new RetentionCounts(0, 0, 0, 0, 0, 0);
        }

        int analyses = await dbContext.AiAnalyses.CountAsync(
            item => item.TenantId == tenantId && leadIds.Contains(item.LeadId),
            cancellationToken);
        int notes = await dbContext.LeadNotes.CountAsync(
            item => item.TenantId == tenantId && leadIds.Contains(item.LeadId),
            cancellationToken);
        int answers = await dbContext.QualificationAnswers.CountAsync(
            item => item.TenantId == tenantId && leadIds.Contains(item.LeadId),
            cancellationToken);
        int actions = await dbContext.ScheduledActions.CountAsync(
            item => item.TenantId == tenantId && leadIds.Contains(item.LeadId),
            cancellationToken);
        int messages = await dbContext.Messages.CountAsync(
            item => item.TenantId == tenantId && leadIds.Contains(item.LeadId),
            cancellationToken);
        int conversations = await dbContext.Conversations.CountAsync(
            item => item.TenantId == tenantId && leadIds.Contains(item.LeadId),
            cancellationToken);
        return new RetentionCounts(
            analyses,
            notes,
            answers,
            actions,
            messages,
            conversations);
    }

    private sealed record RetentionCounts(
        int Analyses,
        int Notes,
        int QualificationAnswers,
        int ScheduledActions,
        int Messages,
        int Conversations);
}
