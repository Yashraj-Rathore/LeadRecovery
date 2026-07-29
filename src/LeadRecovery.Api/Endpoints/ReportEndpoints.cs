using System.Globalization;
using System.Text;

using LeadRecovery.Application.Authorization;
using LeadRecovery.Application.Reporting;
using LeadRecovery.Contracts.Reporting;

namespace LeadRecovery.Api.Endpoints;

internal static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder reports = endpoints.MapGroup("/api/v1/reports")
            .WithTags("Reports")
            .RequireAuthorization(AuthorizationPolicies.TenantMember);
        reports.MapGet("/pilot", Generate);
        reports.MapGet("/pilot.csv", Export);
        return endpoints;
    }

    private static async Task<IResult> Generate(string? from, string? to, PilotReportUseCase useCase, CancellationToken cancellationToken)
    {
        try
        {
            PilotReport report = await useCase.ExecuteAsync(Parse(from), Parse(to), cancellationToken);
            return Results.Ok(new PilotReportResponse(
                report.FromUtc, report.ToUtcExclusive, report.MissedCalls,
                report.RecoveryMessagesSent, report.RecoveryMessagesDelivered,
                report.LeadsWithInboundReply, report.ReplyRatePercent,
                report.QualifiedLeads, report.BookedLeads, report.BookingRatePercent,
                report.ManualMessagesSent, report.FailedMessages, report.OptOuts,
                report.NeedsHumanReview, report.MedianFirstResponseMinutes,
                report.Methodology));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> Export(string? from, string? to, PilotReportUseCase useCase, CancellationToken cancellationToken)
    {
        try
        {
            PilotReport report = await useCase.ExecuteAsync(Parse(from), Parse(to), cancellationToken);
            return Results.File(
                Encoding.UTF8.GetBytes(ToCsv(report)),
                "text/csv; charset=utf-8",
                $"leadrecovery-pilot-{report.FromUtc:yyyyMMdd}-{report.ToUtcExclusive.AddDays(-1):yyyyMMdd}.csv");
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static DateOnly? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            throw new ArgumentException("Dates must use yyyy-MM-dd format.", nameof(value));
        }

        return date;
    }

    private static IResult Validation(ArgumentException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "range"] = [exception.Message],
        });

    private static string ToCsv(PilotReport report)
    {
        StringBuilder csv = new();
        csv.AppendLine("metric,value");
        Append(csv, "period_start_utc", report.FromUtc.ToString("O", CultureInfo.InvariantCulture));
        Append(csv, "period_end_utc_exclusive", report.ToUtcExclusive.ToString("O", CultureInfo.InvariantCulture));
        Append(csv, "missed_calls", report.MissedCalls);
        Append(csv, "recovery_messages_sent", report.RecoveryMessagesSent);
        Append(csv, "recovery_messages_delivered", report.RecoveryMessagesDelivered);
        Append(csv, "leads_with_inbound_reply", report.LeadsWithInboundReply);
        Append(csv, "reply_rate_percent", report.ReplyRatePercent);
        Append(csv, "qualified_leads", report.QualifiedLeads);
        Append(csv, "booked_leads", report.BookedLeads);
        Append(csv, "booking_rate_percent", report.BookingRatePercent);
        Append(csv, "manual_messages_sent", report.ManualMessagesSent);
        Append(csv, "failed_messages", report.FailedMessages);
        Append(csv, "opt_outs", report.OptOuts);
        Append(csv, "needs_human_review", report.NeedsHumanReview);
        Append(csv, "median_first_response_minutes", report.MedianFirstResponseMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        Append(csv, "methodology", report.Methodology);
        return csv.ToString();
    }

    private static void Append(StringBuilder csv, string metric, object value)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        char quote = (char)34;
        csv.Append(metric).Append(',').Append(quote)
            .Append(text.Replace(quote.ToString(), new string(quote, 2), StringComparison.Ordinal))
            .Append(quote).AppendLine();
    }
}
