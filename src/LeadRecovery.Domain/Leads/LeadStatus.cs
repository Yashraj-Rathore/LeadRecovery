namespace LeadRecovery.Domain.Leads;

public enum LeadStatus
{
    New,
    Contacting,
    AwaitingCustomer,
    Qualified,
    BookingOffered,
    NeedsHuman,
    Booked,
    Closed,
    ClosedWon,
}
