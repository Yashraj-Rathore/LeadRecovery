using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Domain.Tests;

public sealed class LeadStatusTransitionPolicyTests
{
    private static readonly HashSet<(LeadStatus Current, LeadStatus Target)>
        AllowedTransitions =
        [
            (LeadStatus.New, LeadStatus.Contacting),
            (LeadStatus.New, LeadStatus.NeedsHuman),
            (LeadStatus.New, LeadStatus.Closed),
            (LeadStatus.Contacting, LeadStatus.AwaitingCustomer),
            (LeadStatus.Contacting, LeadStatus.NeedsHuman),
            (LeadStatus.Contacting, LeadStatus.Closed),
            (LeadStatus.AwaitingCustomer, LeadStatus.Qualified),
            (LeadStatus.AwaitingCustomer, LeadStatus.NeedsHuman),
            (LeadStatus.AwaitingCustomer, LeadStatus.Closed),
            (LeadStatus.Qualified, LeadStatus.BookingOffered),
            (LeadStatus.Qualified, LeadStatus.NeedsHuman),
            (LeadStatus.Qualified, LeadStatus.Closed),
            (LeadStatus.BookingOffered, LeadStatus.NeedsHuman),
            (LeadStatus.BookingOffered, LeadStatus.Booked),
            (LeadStatus.BookingOffered, LeadStatus.Closed),
            (LeadStatus.NeedsHuman, LeadStatus.Qualified),
            (LeadStatus.NeedsHuman, LeadStatus.Booked),
            (LeadStatus.NeedsHuman, LeadStatus.Closed),
            (LeadStatus.Booked, LeadStatus.ClosedWon),
        ];

    public static TheoryData<LeadStatus, LeadStatus, bool> EveryStatusPair
    {
        get
        {
            TheoryData<LeadStatus, LeadStatus, bool> data = [];
            foreach (LeadStatus current in Enum.GetValues<LeadStatus>())
            {
                foreach (LeadStatus target in Enum.GetValues<LeadStatus>())
                {
                    data.Add(current, target, AllowedTransitions.Contains((current, target)));
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryStatusPair))]
    public void PolicyDefinesEveryStatusPair(
        LeadStatus current,
        LeadStatus target,
        bool expected)
    {
        Assert.Equal(expected, LeadStatusTransitionPolicy.CanTransition(current, target));
    }

    [Theory]
    [InlineData(LeadStatus.Closed)]
    [InlineData(LeadStatus.ClosedWon)]
    public void ClosedOutcomesAreTerminal(LeadStatus status)
    {
        Assert.True(LeadStatusTransitionPolicy.IsTerminal(status));
    }

    [Theory]
    [InlineData(LeadStatus.New)]
    [InlineData(LeadStatus.Contacting)]
    [InlineData(LeadStatus.AwaitingCustomer)]
    [InlineData(LeadStatus.Qualified)]
    [InlineData(LeadStatus.BookingOffered)]
    [InlineData(LeadStatus.NeedsHuman)]
    public void PreBookingStatesAreActive(LeadStatus status)
    {
        Assert.True(LeadStatusTransitionPolicy.IsPreBookingActive(status));
    }

    [Fact]
    public void PolicyRejectsUndefinedStatus()
    {
        LeadStatus undefined = (LeadStatus)int.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => LeadStatusTransitionPolicy.CanTransition(undefined, LeadStatus.New));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LeadStatusTransitionPolicy.CanTransition(LeadStatus.New, undefined));
    }
}
