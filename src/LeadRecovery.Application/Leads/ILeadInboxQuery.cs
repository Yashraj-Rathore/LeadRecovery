namespace LeadRecovery.Application.Leads;

public interface ILeadInboxQuery
{
    Task<LeadInboxPage> ListAsync(
        int pageSize,
        string? cursor,
        LeadInboxCriteria criteria,
        CancellationToken cancellationToken);

    Task<LeadInboxItem?> GetAsync(Guid leadId, CancellationToken cancellationToken);
}
