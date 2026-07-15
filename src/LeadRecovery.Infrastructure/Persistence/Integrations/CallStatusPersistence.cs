using System.Data;

using LeadRecovery.Application.Integrations;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeadRecovery.Infrastructure.Persistence.Integrations;

internal sealed class CallStatusPersistence(LeadRecoveryDbContext dbContext)
    : ICallStatusPersistence
{
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        await operation(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> TryAddReceiptAsync(
        ExternalEventReceipt receipt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        int inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into external_event_receipts
                (id, tenant_id, provider, event_type, external_event_id,
                 payload_hash, received_at_utc)
            values
                ({receipt.Id}, {receipt.TenantId}, {receipt.Provider},
                 {receipt.EventType}, {receipt.ExternalEventId},
                 {receipt.PayloadHash}, {receipt.ReceivedAtUtc})
            on conflict (provider, event_type, external_event_id) do nothing
            """,
            cancellationToken);
        if (inserted == 0)
        {
            return false;
        }

        dbContext.Attach(receipt);
        return true;
    }

    public async Task<TenantPhoneRecoveryRoute?> FindRouteAsync(
        string provider,
        string destinationPhoneE164,
        CancellationToken cancellationToken)
    {
        var route = await (
            from number in dbContext.TenantPhoneNumbers.IgnoreQueryFilters()
            join tenant in dbContext.Tenants on number.TenantId equals tenant.Id
            where number.Provider == provider &&
                number.PhoneNumberE164 == destinationPhoneE164
            select new
            {
                number.TenantId,
                tenant.Status,
                tenant.AutomationEnabled,
                number.MissedCallRecoveryEnabled,
                number.RecoverableCallStatuses,
                number.InitialDelaySeconds,
                number.RecoveryCooldownSeconds,
            }).SingleOrDefaultAsync(cancellationToken);

        if (route is null)
        {
            return null;
        }

        bool tenantCanOperate =
            (route.Status is TenantStatus.Trial or TenantStatus.Active) &&
            route.AutomationEnabled &&
            route.MissedCallRecoveryEnabled;
        return new TenantPhoneRecoveryRoute(
            route.TenantId,
            tenantCanOperate,
            route.RecoverableCallStatuses,
            route.InitialDelaySeconds,
            route.RecoveryCooldownSeconds);
    }

    public Task<Lead?> FindLatestLeadAsync(
        string callerPhoneE164,
        CancellationToken cancellationToken) =>
        dbContext.Leads
            .Where(lead => lead.PrimaryPhoneE164 == callerPhoneE164)
            .OrderByDescending(lead => lead.UpdatedAtUtc)
            .ThenByDescending(lead => lead.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasRecoveryActionSinceAsync(
        string callerPhoneE164,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken) =>
        (
            from action in dbContext.ScheduledActions
            join lead in dbContext.Leads on
                new { action.TenantId, Id = action.LeadId }
                equals new { lead.TenantId, lead.Id }
            where action.ActionType == ProcessCallStatusWebhookUseCase.RecoveryActionType &&
                action.CreatedAtUtc >= sinceUtc &&
                lead.PrimaryPhoneE164 == callerPhoneE164
            select action.Id).AnyAsync(cancellationToken);

    public void AddLead(Lead lead) => dbContext.Leads.Add(lead);

    public void AddScheduledAction(ScheduledAction action) =>
        dbContext.ScheduledActions.Add(action);

    public void AddAuditEvent(AuditEvent auditEvent) =>
        dbContext.AuditEvents.Add(auditEvent);
}
