namespace LeadRecovery.Application.Leads;

public interface ILeadAutomationCancellation
{
    Task CancelPendingAsync(
        Guid tenantId,
        Guid leadId,
        CancellationToken cancellationToken);
}
