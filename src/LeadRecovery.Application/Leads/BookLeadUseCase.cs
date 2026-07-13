using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Leads;

public sealed class BookLeadUseCase
{
    private readonly ILeadAutomationCancellation _automationCancellation;

    public BookLeadUseCase(ILeadAutomationCancellation automationCancellation)
    {
        ArgumentNullException.ThrowIfNull(automationCancellation);
        _automationCancellation = automationCancellation;
    }

    public async Task ExecuteAsync(
        Lead lead,
        DateTimeOffset bookedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lead);
        cancellationToken.ThrowIfCancellationRequested();

        lead.Book(bookedAtUtc);
        await _automationCancellation.CancelPendingAsync(
            lead.TenantId,
            lead.Id,
            cancellationToken);
    }
}
