using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Integrations;

public interface ICallStatusPersistence
{
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);

    Task<bool> TryAddReceiptAsync(
        ExternalEventReceipt receipt,
        CancellationToken cancellationToken);

    Task<TenantPhoneRecoveryRoute?> FindRouteAsync(
        string provider,
        string destinationPhoneE164,
        CancellationToken cancellationToken);

    Task<Lead?> FindLatestLeadAsync(
        string callerPhoneE164,
        CancellationToken cancellationToken);

    Task<bool> HasRecoveryActionSinceAsync(
        string callerPhoneE164,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken);

    void AddLead(Lead lead);

    void AddScheduledAction(ScheduledAction action);

    void AddAuditEvent(AuditEvent auditEvent);
}
