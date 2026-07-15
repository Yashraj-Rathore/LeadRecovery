using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Domain.Tests;

public sealed class LeadTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 13, 16, 0, 0, TimeSpan.Zero);

    public static TheoryData<LeadStatus, LeadStatus> AllowedTransitions
    {
        get
        {
            TheoryData<LeadStatus, LeadStatus> data = [];
            foreach (LeadStatus current in Enum.GetValues<LeadStatus>())
            {
                foreach (LeadStatus target in Enum.GetValues<LeadStatus>())
                {
                    if (LeadStatusTransitionPolicy.CanTransition(current, target))
                    {
                        data.Add(current, target);
                    }
                }
            }

            return data;
        }
    }

    [Fact]
    public void ConstructorCreatesSafeNewLead()
    {
        Guid leadId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();

        Lead lead = new(
            leadId,
            tenantId,
            " +14165550123 ",
            LeadSource.MissedCall,
            CreatedAtUtc,
            " Alex Customer ");

        Assert.Equal(leadId, lead.Id);
        Assert.Equal(tenantId, lead.TenantId);
        Assert.Equal("+14165550123", lead.PrimaryPhoneE164);
        Assert.Equal("Alex Customer", lead.DisplayName);
        Assert.Equal(LeadSource.MissedCall, lead.Source);
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Equal(LeadUrgency.Unknown, lead.Urgency);
        Assert.Equal(AutomationState.Active, lead.AutomationState);
        Assert.Equal(0, lead.Version);
        Assert.Equal(CreatedAtUtc, lead.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, lead.UpdatedAtUtc);
        Assert.Null(lead.CustomerId);
        Assert.Null(lead.ServiceCategoryId);
        Assert.Null(lead.AssignedUserId);
        Assert.Null(lead.LastCustomerActivityAtUtc);
        Assert.Null(lead.LastBusinessActivityAtUtc);
        Assert.Null(lead.BookedAtUtc);
        Assert.Null(lead.ClosedAtUtc);
        Assert.Null(lead.CloseReason);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ConstructorRejectsEmptyRequiredId(bool emptyLeadId, bool emptyTenantId)
    {
        Guid leadId = emptyLeadId ? Guid.Empty : Guid.CreateVersion7();
        Guid tenantId = emptyTenantId ? Guid.Empty : Guid.CreateVersion7();

        Assert.Throws<ArgumentException>(() => new Lead(
            leadId,
            tenantId,
            "+14165550123",
            LeadSource.MissedCall,
            CreatedAtUtc));
    }

    [Fact]
    public void ConstructorRejectsMissingPhone()
    {
        Assert.Throws<ArgumentException>(() => new Lead(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            " ",
            LeadSource.MissedCall,
            CreatedAtUtc));
    }

    [Fact]
    public void ConstructorRejectsUndefinedSource()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Lead(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "+14165550123",
            (LeadSource)int.MaxValue,
            CreatedAtUtc));
    }

    [Fact]
    public void ConstructorRejectsNonUtcTimestamp()
    {
        Assert.Throws<ArgumentException>(() => new Lead(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "+14165550123",
            LeadSource.MissedCall,
            CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
    }

    [Fact]
    public void CustomerActivityUpdatesLeadWithoutChangingStatus()
    {
        Lead lead = CreateLeadInStatus(LeadStatus.New);
        DateTimeOffset activityAtUtc = lead.UpdatedAtUtc.AddMinutes(1);

        lead.RecordCustomerActivity(activityAtUtc);

        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Equal(activityAtUtc, lead.LastCustomerActivityAtUtc);
        Assert.Equal(activityAtUtc, lead.UpdatedAtUtc);
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void AggregateImplementsEveryAllowedTransition(
        LeadStatus current,
        LeadStatus target)
    {
        Lead lead = CreateLeadInStatus(current);
        DateTimeOffset changedAtUtc = lead.UpdatedAtUtc.AddMinutes(1);

        ApplyTransition(lead, target, changedAtUtc);

        Assert.Equal(target, lead.Status);
        Assert.Equal(changedAtUtc, lead.UpdatedAtUtc);
    }

    [Fact]
    public void QualificationRequiresDetailsOrStaffOverrideReason()
    {
        Lead lead = CreateLeadInStatus(LeadStatus.AwaitingCustomer);

        Assert.Throws<ArgumentException>(() => lead.Qualify(
            minimumRequiredDetailsPresent: false,
            staffOverrideReason: " ",
            lead.UpdatedAtUtc.AddMinutes(1)));
        Assert.Equal(LeadStatus.AwaitingCustomer, lead.Status);

        lead.Qualify(
            minimumRequiredDetailsPresent: false,
            staffOverrideReason: "Staff confirmed details by phone.",
            lead.UpdatedAtUtc.AddMinutes(1));
        Assert.Equal(LeadStatus.Qualified, lead.Status);
    }

    [Theory]
    [InlineData(LeadStatus.BookingOffered)]
    [InlineData(LeadStatus.NeedsHuman)]
    public void BookingCompletesAutomation(LeadStatus status)
    {
        Lead lead = CreateLeadInStatus(status);
        DateTimeOffset bookedAtUtc = lead.UpdatedAtUtc.AddMinutes(1);

        lead.Book(bookedAtUtc);

        Assert.Equal(LeadStatus.Booked, lead.Status);
        Assert.Equal(AutomationState.Completed, lead.AutomationState);
        Assert.Equal(bookedAtUtc, lead.BookedAtUtc);
        Assert.Null(lead.CloseReason);
        Assert.Null(lead.ClosedAtUtc);
    }

    [Theory]
    [InlineData(LeadStatus.New)]
    [InlineData(LeadStatus.Contacting)]
    [InlineData(LeadStatus.AwaitingCustomer)]
    [InlineData(LeadStatus.Qualified)]
    [InlineData(LeadStatus.BookingOffered)]
    [InlineData(LeadStatus.NeedsHuman)]
    public void EveryPreBookingStateCanCloseWithReason(LeadStatus status)
    {
        Lead lead = CreateLeadInStatus(status);
        DateTimeOffset closedAtUtc = lead.UpdatedAtUtc.AddMinutes(1);

        lead.Close(LeadCloseReason.LostNoResponse, closedAtUtc);

        Assert.Equal(LeadStatus.Closed, lead.Status);
        Assert.Equal(LeadCloseReason.LostNoResponse, lead.CloseReason);
        Assert.Equal(AutomationState.Completed, lead.AutomationState);
        Assert.Equal(closedAtUtc, lead.ClosedAtUtc);
    }

    [Fact]
    public void OptOutClosureSuppressesAutomation()
    {
        Lead lead = CreateLead();

        lead.Close(LeadCloseReason.OptedOut, CreatedAtUtc.AddMinutes(1));

        Assert.Equal(AutomationState.SuppressedOptOut, lead.AutomationState);
    }

    [Fact]
    public void ClosureRejectsUndefinedReason()
    {
        Lead lead = CreateLead();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            lead.Close((LeadCloseReason)int.MaxValue, CreatedAtUtc.AddMinutes(1)));
        Assert.Equal(LeadStatus.New, lead.Status);
    }

    [Fact]
    public void CloseReasonsDoNotContainSuccessfulStatuses()
    {
        string[] reasonNames = Enum.GetNames<LeadCloseReason>();

        Assert.DoesNotContain(nameof(LeadStatus.Booked), reasonNames);
        Assert.DoesNotContain(nameof(LeadStatus.ClosedWon), reasonNames);
    }

    [Fact]
    public void BookedLeadCanBeConfirmedWon()
    {
        Lead lead = CreateLeadInStatus(LeadStatus.Booked);
        DateTimeOffset confirmedAtUtc = lead.UpdatedAtUtc.AddMinutes(1);

        lead.ConfirmWon(confirmedAtUtc);

        Assert.Equal(LeadStatus.ClosedWon, lead.Status);
        Assert.Equal(confirmedAtUtc, lead.ClosedAtUtc);
        Assert.Equal(AutomationState.Completed, lead.AutomationState);
    }

    [Theory]
    [InlineData(LeadStatus.Closed)]
    [InlineData(LeadStatus.ClosedWon)]
    public void TerminalLeadCannotReopen(LeadStatus status)
    {
        Lead lead = CreateLeadInStatus(status);

        Assert.Throws<InvalidOperationException>(() =>
            lead.RequireHumanReview(lead.UpdatedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void TransitionRejectsTimestampMovingBackwards()
    {
        Lead lead = CreateLead();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            lead.BeginContacting(CreatedAtUtc.AddTicks(-1)));
        Assert.Equal(LeadStatus.New, lead.Status);
    }

    [Fact]
    public void TransitionRejectsNonUtcTimestamp()
    {
        Lead lead = CreateLead();

        Assert.Throws<ArgumentException>(() =>
            lead.BeginContacting(CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
        Assert.Equal(LeadStatus.New, lead.Status);
    }

    private static Lead CreateLead() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "+14165550123",
            LeadSource.MissedCall,
            CreatedAtUtc);

    private static Lead CreateLeadInStatus(LeadStatus status)
    {
        Lead lead = CreateLead();
        DateTimeOffset changedAtUtc = CreatedAtUtc;

        void Next(Action<DateTimeOffset> transition)
        {
            changedAtUtc = changedAtUtc.AddMinutes(1);
            transition(changedAtUtc);
        }

        switch (status)
        {
            case LeadStatus.New:
                break;
            case LeadStatus.Contacting:
                Next(lead.BeginContacting);
                break;
            case LeadStatus.AwaitingCustomer:
                Next(lead.BeginContacting);
                Next(lead.AwaitCustomer);
                break;
            case LeadStatus.Qualified:
                Next(lead.BeginContacting);
                Next(lead.AwaitCustomer);
                changedAtUtc = changedAtUtc.AddMinutes(1);
                lead.Qualify(true, null, changedAtUtc);
                break;
            case LeadStatus.BookingOffered:
                Next(lead.BeginContacting);
                Next(lead.AwaitCustomer);
                changedAtUtc = changedAtUtc.AddMinutes(1);
                lead.Qualify(true, null, changedAtUtc);
                Next(lead.OfferBooking);
                break;
            case LeadStatus.NeedsHuman:
                Next(lead.RequireHumanReview);
                break;
            case LeadStatus.Booked:
                Next(lead.RequireHumanReview);
                Next(lead.Book);
                break;
            case LeadStatus.Closed:
                changedAtUtc = changedAtUtc.AddMinutes(1);
                lead.Close(LeadCloseReason.LostNoResponse, changedAtUtc);
                break;
            case LeadStatus.ClosedWon:
                Next(lead.RequireHumanReview);
                Next(lead.Book);
                Next(lead.ConfirmWon);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return lead;
    }

    private static void ApplyTransition(
        Lead lead,
        LeadStatus target,
        DateTimeOffset changedAtUtc)
    {
        switch (target)
        {
            case LeadStatus.Contacting:
                lead.BeginContacting(changedAtUtc);
                break;
            case LeadStatus.AwaitingCustomer:
                lead.AwaitCustomer(changedAtUtc);
                break;
            case LeadStatus.Qualified:
                lead.Qualify(true, null, changedAtUtc);
                break;
            case LeadStatus.BookingOffered:
                lead.OfferBooking(changedAtUtc);
                break;
            case LeadStatus.NeedsHuman:
                lead.RequireHumanReview(changedAtUtc);
                break;
            case LeadStatus.Booked:
                lead.Book(changedAtUtc);
                break;
            case LeadStatus.Closed:
                lead.Close(LeadCloseReason.LostNoResponse, changedAtUtc);
                break;
            case LeadStatus.ClosedWon:
                lead.ConfirmWon(changedAtUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}
