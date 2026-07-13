namespace LeadRecovery.Domain.Leads;

public enum LeadCloseReason
{
    LostNoResponse,
    LostOutOfArea,
    LostUnavailableService,
    Duplicate,
    Spam,
    OptedOut,
}
