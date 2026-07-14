namespace LeadRecovery.Application.Leads;

public sealed class ListLeadsUseCase(ILeadInboxQuery query)
{
    public Task<LeadInboxPage> ExecuteAsync(
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                "Page size must be between 1 and 100.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return query.ListAsync(pageSize, cursor, cancellationToken);
    }
}
