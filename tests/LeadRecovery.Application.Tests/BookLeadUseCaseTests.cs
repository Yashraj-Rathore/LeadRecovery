using LeadRecovery.Application.Leads;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Tests;

public sealed class BookLeadUseCaseTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 13, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorRejectsMissingAutomationCancellation()
    {
        Assert.Throws<ArgumentNullException>(() => new BookLeadUseCase(null!));
    }

    [Fact]
    public async Task ExecuteBooksLeadAndCancelsPendingAutomation()
    {
        Lead lead = CreateBookingOfferedLead();
        RecordingAutomationCancellation cancellation = new();
        BookLeadUseCase useCase = new(cancellation);
        using CancellationTokenSource cancellationSource = new();
        DateTimeOffset bookedAtUtc = lead.UpdatedAtUtc.AddMinutes(1);

        await useCase.ExecuteAsync(lead, bookedAtUtc, cancellationSource.Token);

        Assert.Equal(LeadStatus.Booked, lead.Status);
        Assert.Equal(AutomationState.Completed, lead.AutomationState);
        Assert.Equal(bookedAtUtc, lead.BookedAtUtc);
        Assert.Equal(lead.TenantId, cancellation.TenantId);
        Assert.Equal(lead.Id, cancellation.LeadId);
        Assert.Equal(cancellationSource.Token, cancellation.CancellationToken);
        Assert.Equal(1, cancellation.CallCount);
    }

    [Fact]
    public async Task ExecuteRejectsInvalidTransitionWithoutCancellingAutomation()
    {
        Lead lead = CreateLead();
        RecordingAutomationCancellation cancellation = new();
        BookLeadUseCase useCase = new(cancellation);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(
                lead,
                lead.UpdatedAtUtc.AddMinutes(1),
                TestContext.Current.CancellationToken));

        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Equal(0, cancellation.CallCount);
    }

    [Fact]
    public async Task ExecuteHonorsPreCancelledTokenBeforeMutation()
    {
        Lead lead = CreateBookingOfferedLead();
        RecordingAutomationCancellation cancellation = new();
        BookLeadUseCase useCase = new(cancellation);
        using CancellationTokenSource cancellationSource = new();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(
                lead,
                lead.UpdatedAtUtc.AddMinutes(1),
                cancellationSource.Token));

        Assert.Equal(LeadStatus.BookingOffered, lead.Status);
        Assert.Equal(0, cancellation.CallCount);
    }

    private static Lead CreateLead() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "+14165550123",
            LeadSource.MissedCall,
            CreatedAtUtc);

    private static Lead CreateBookingOfferedLead()
    {
        Lead lead = CreateLead();
        lead.BeginContacting(CreatedAtUtc.AddMinutes(1));
        lead.AwaitCustomer(CreatedAtUtc.AddMinutes(2));
        lead.Qualify(true, null, CreatedAtUtc.AddMinutes(3));
        lead.OfferBooking(CreatedAtUtc.AddMinutes(4));
        return lead;
    }

    private sealed class RecordingAutomationCancellation : ILeadAutomationCancellation
    {
        public Guid TenantId { get; private set; }

        public Guid LeadId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public int CallCount { get; private set; }

        public Task CancelPendingAsync(
            Guid tenantId,
            Guid leadId,
            CancellationToken cancellationToken)
        {
            TenantId = tenantId;
            LeadId = leadId;
            CancellationToken = cancellationToken;
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
