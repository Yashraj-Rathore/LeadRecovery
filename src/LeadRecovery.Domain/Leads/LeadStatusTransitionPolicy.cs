namespace LeadRecovery.Domain.Leads;

public static class LeadStatusTransitionPolicy
{
    public static bool CanTransition(LeadStatus current, LeadStatus target)
    {
        EnsureDefined(current, nameof(current));
        EnsureDefined(target, nameof(target));

        if (current == target || IsTerminal(current))
        {
            return false;
        }

        if (target == LeadStatus.NeedsHuman)
        {
            return IsPreBookingActive(current) && current != LeadStatus.NeedsHuman;
        }

        if (target == LeadStatus.Closed)
        {
            return IsPreBookingActive(current);
        }

        return (current, target) switch
        {
            (LeadStatus.New, LeadStatus.Contacting) => true,
            (LeadStatus.Contacting, LeadStatus.AwaitingCustomer) => true,
            (LeadStatus.AwaitingCustomer, LeadStatus.Qualified) => true,
            (LeadStatus.Qualified, LeadStatus.BookingOffered) => true,
            (LeadStatus.NeedsHuman, LeadStatus.Qualified) => true,
            (LeadStatus.BookingOffered, LeadStatus.Booked) => true,
            (LeadStatus.NeedsHuman, LeadStatus.Booked) => true,
            (LeadStatus.Booked, LeadStatus.ClosedWon) => true,
            _ => false,
        };
    }

    public static bool IsTerminal(LeadStatus status)
    {
        EnsureDefined(status, nameof(status));
        return status is LeadStatus.Closed or LeadStatus.ClosedWon;
    }

    public static bool IsPreBookingActive(LeadStatus status)
    {
        EnsureDefined(status, nameof(status));
        return status is
            LeadStatus.New or
            LeadStatus.Contacting or
            LeadStatus.AwaitingCustomer or
            LeadStatus.Qualified or
            LeadStatus.BookingOffered or
            LeadStatus.NeedsHuman;
    }

    private static void EnsureDefined(LeadStatus status, string parameterName)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
