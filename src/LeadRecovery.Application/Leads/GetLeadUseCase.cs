namespace LeadRecovery.Application.Leads;

public sealed class GetLeadUseCase(ILeadInboxQuery query)
{
    public Task<LeadInboxItem?> ExecuteAsync(
        Guid leadId,
        CancellationToken cancellationToken)
    {
        if (leadId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty lead ID is required.", nameof(leadId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return query.GetAsync(leadId, cancellationToken);
    }
}
