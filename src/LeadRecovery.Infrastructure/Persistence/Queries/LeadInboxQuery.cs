using System.Globalization;
using System.Text;

using LeadRecovery.Application.Leads;
using LeadRecovery.Domain.Leads;

using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Infrastructure.Persistence.Queries;

internal sealed class LeadInboxQuery(LeadRecoveryDbContext dbContext)
    : ILeadInboxQuery
{
    public async Task<LeadInboxPage> ListAsync(
        int pageSize,
        string? cursor,
        LeadInboxCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        int offset = DecodeCursor(cursor);
        IQueryable<Lead> leads = dbContext.Leads.AsNoTracking();
        if (criteria.Status is LeadStatus status)
        {
            leads = leads.Where(lead => lead.Status == status);
        }

        if (criteria.Urgency is LeadUrgency urgency)
        {
            leads = leads.Where(lead => lead.Urgency == urgency);
        }

        leads = criteria.Assignment switch
        {
            LeadAssignmentFilter.All => leads,
            LeadAssignmentFilter.Unassigned =>
                leads.Where(lead => lead.AssignedUserId == null),
            LeadAssignmentFilter.Mine =>
                leads.Where(lead => lead.AssignedUserId == criteria.CurrentUserId),
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        if (criteria.AssignedUserId is Guid assignedUserId)
        {
            leads = leads.Where(lead => lead.AssignedUserId == assignedUserId);
        }

        List<LeadInboxItem> items = await leads
            .AsNoTracking()
            .OrderByDescending(lead => lead.Status == LeadStatus.NeedsHuman)
            .ThenByDescending(lead => lead.Urgency)
            .ThenByDescending(lead =>
                lead.LastCustomerActivityAtUtc ??
                lead.LastBusinessActivityAtUtc ??
                lead.CreatedAtUtc)
            .ThenByDescending(lead => lead.Id)
            .Skip(offset)
            .Take(pageSize + 1)
            .Select(lead => new LeadInboxItem(
                lead.Id,
                lead.DisplayName,
                lead.PrimaryPhoneE164,
                lead.Source,
                lead.Status,
                lead.Urgency,
                lead.AutomationState,
                lead.AssignedUserId,
                lead.AssignedUserId == null
                    ? null
                    : dbContext.Users
                        .Where(user => user.Id == lead.AssignedUserId)
                        .Select(user => user.DisplayName)
                        .SingleOrDefault(),
                lead.LastCustomerActivityAtUtc ??
                    lead.LastBusinessActivityAtUtc ??
                    lead.CreatedAtUtc,
                lead.LastCustomerActivityAtUtc != null &&
                    (lead.LastBusinessActivityAtUtc == null ||
                        lead.LastCustomerActivityAtUtc > lead.LastBusinessActivityAtUtc),
                lead.Version,
                lead.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        bool hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        string? nextCursor = hasMore ? EncodeCursor(checked(offset + pageSize)) : null;
        return new LeadInboxPage(items, nextCursor);
    }

    public Task<LeadInboxItem?> GetAsync(
        Guid leadId,
        CancellationToken cancellationToken) =>
        dbContext.Leads
            .AsNoTracking()
            .Where(lead => lead.Id == leadId)
            .Select(lead => new LeadInboxItem(
                lead.Id,
                lead.DisplayName,
                lead.PrimaryPhoneE164,
                lead.Source,
                lead.Status,
                lead.Urgency,
                lead.AutomationState,
                lead.AssignedUserId,
                lead.AssignedUserId == null
                    ? null
                    : dbContext.Users
                        .Where(user => user.Id == lead.AssignedUserId)
                        .Select(user => user.DisplayName)
                        .SingleOrDefault(),
                lead.LastCustomerActivityAtUtc ??
                    lead.LastBusinessActivityAtUtc ??
                    lead.CreatedAtUtc,
                lead.LastCustomerActivityAtUtc != null &&
                    (lead.LastBusinessActivityAtUtc == null ||
                        lead.LastCustomerActivityAtUtc > lead.LastBusinessActivityAtUtc),
                lead.Version,
                lead.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        try
        {
            string value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (!int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int offset) || offset < 0)
            {
                throw new FormatException();
            }

            return offset;
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The lead cursor is invalid.", nameof(cursor), exception);
        }
    }

    private static string EncodeCursor(int offset) =>
        Convert.ToBase64String(
            Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));
}
