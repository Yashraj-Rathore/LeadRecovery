using LeadRecovery.Application.Leads;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Automations;

using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Infrastructure.Persistence.Automations;

internal sealed class ScheduledActionLeadAutomationCancellation(
    LeadRecoveryDbContext dbContext,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : ILeadAutomationCancellation
{
    public async Task CancelPendingAsync(
        Guid tenantId,
        Guid leadId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty tenant ID is required.", nameof(tenantId));
        }

        if (leadId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty lead ID is required.", nameof(leadId));
        }

        if (tenantContext.TenantId != tenantId)
        {
            throw new InvalidOperationException(
                "Pending automation cannot be cancelled outside the active tenant.");
        }

        List<ScheduledAction> pendingActions = await dbContext.ScheduledActions
            .Where(action =>
                action.TenantId == tenantId &&
                action.LeadId == leadId &&
                action.Status == ScheduledActionStatus.Pending)
            .ToListAsync(cancellationToken);
        DateTimeOffset cancelledAtUtc = timeProvider.GetUtcNow();
        foreach (ScheduledAction action in pendingActions)
        {
            action.Cancel(cancelledAtUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
