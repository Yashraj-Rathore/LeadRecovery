using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Reporting;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Leads;

using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Infrastructure.Persistence.Reporting;

internal sealed class PilotReportStore(
    LeadRecoveryDbContext dbContext,
    ITenantContext tenantContext) : IPilotReportStore
{
    private const string Methodology =
        "Operational indicators from tenant-scoped workflow records. Reply and booking rates use missed calls as the baseline; bookings require staff confirmation. Values do not estimate revenue or prove that LeadRecovery caused an outcome.";

    public async Task<PilotReport> GenerateAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken)
    {
        var missedLeads = await dbContext.Leads.AsNoTracking()
            .Where(lead => lead.Source == LeadSource.MissedCall && lead.CreatedAtUtc >= fromUtc && lead.CreatedAtUtc < toUtcExclusive)
            .Select(lead => new { lead.Id, lead.Status, lead.CreatedAtUtc, lead.BookedAtUtc })
            .ToArrayAsync(cancellationToken);
        Guid[] leadIds = missedLeads.Select(lead => lead.Id).ToArray();
        var messages = await dbContext.Messages.AsNoTracking()
            .Where(message => leadIds.Contains(message.LeadId) && message.CreatedAtUtc >= fromUtc && message.CreatedAtUtc < toUtcExclusive)
            .Select(message => new
            {
                message.LeadId,
                message.Direction,
                message.Kind,
                message.Status,
                message.TemplateId,
                message.CreatedAtUtc,
                message.SentAtUtc,
            })
            .ToArrayAsync(cancellationToken);

        Guid[] templateIds = messages.Where(message => message.TemplateId.HasValue)
            .Select(message => message.TemplateId!.Value)
            .Distinct()
            .ToArray();
        Dictionary<Guid, string> purposes = await dbContext.MessageTemplates.AsNoTracking()
            .Where(template => templateIds.Contains(template.Id))
            .ToDictionaryAsync(template => template.Id, template => template.Purpose, cancellationToken);
        var recoveryMessages = messages.Where(message =>
            message.Direction == MessageDirection.Outbound &&
            message.TemplateId is Guid templateId &&
            purposes.TryGetValue(templateId, out string? purpose) &&
            purpose == SmsTemplatePurposes.InitialMissedCallRecovery).ToArray();

        int recoverySent = recoveryMessages.Count(message => message.Status is MessageStatus.Sent or MessageStatus.Delivered);
        int replied = messages.Where(message => message.Direction == MessageDirection.Inbound)
            .Select(message => message.LeadId)
            .Distinct()
            .Count();
        int booked = missedLeads.Count(lead => lead.BookedAtUtc is not null);
        decimal? medianResponse = CalculateMedian(missedLeads.Select(lead =>
            {
                DateTimeOffset? firstSent = recoveryMessages
                    .Where(message => message.LeadId == lead.Id && message.SentAtUtc.HasValue)
                    .Select(message => message.SentAtUtc)
                    .Min();
                return firstSent.HasValue ? (decimal?)(firstSent.Value - lead.CreatedAtUtc).TotalMinutes : null;
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray());
        int optOuts = await dbContext.AuditEvents.AsNoTracking().CountAsync(
            audit =>
                audit.TenantId == tenantContext.TenantId &&
                audit.Action == "CustomerSmsOptedOut" &&
                audit.CreatedAtUtc >= fromUtc &&
                audit.CreatedAtUtc < toUtcExclusive,
            cancellationToken);
        int denominator = missedLeads.Length;

        return new PilotReport(
            fromUtc,
            toUtcExclusive,
            denominator,
            recoverySent,
            recoveryMessages.Count(message => message.Status == MessageStatus.Delivered),
            replied,
            Percentage(replied, denominator),
            missedLeads.Count(lead => lead.Status is LeadStatus.Qualified or LeadStatus.BookingOffered or LeadStatus.Booked or LeadStatus.ClosedWon),
            booked,
            Percentage(booked, denominator),
            messages.Count(message =>
                message.Direction == MessageDirection.Outbound &&
                message.Kind == MessageKind.Manual &&
                message.Status is MessageStatus.Sent or MessageStatus.Delivered),
            messages.Count(message => message.Status == MessageStatus.Failed),
            optOuts,
            missedLeads.Count(lead => lead.Status == LeadStatus.NeedsHuman),
            medianResponse,
            Methodology);
    }

    private static decimal Percentage(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round(100m * numerator / denominator, 1);

    private static decimal? CalculateMedian(decimal[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        Array.Sort(values);
        int midpoint = values.Length / 2;
        return values.Length % 2 == 1
            ? Math.Round(values[midpoint], 1)
            : Math.Round((values[midpoint - 1] + values[midpoint]) / 2, 1);
    }
}
