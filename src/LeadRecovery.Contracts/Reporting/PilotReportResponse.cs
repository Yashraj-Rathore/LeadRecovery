namespace LeadRecovery.Contracts.Reporting;

public sealed record PilotReportResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtcExclusive,
    int MissedCalls,
    int RecoveryMessagesSent,
    int RecoveryMessagesDelivered,
    int LeadsWithInboundReply,
    decimal ReplyRatePercent,
    int QualifiedLeads,
    int BookedLeads,
    decimal BookingRatePercent,
    int ManualMessagesSent,
    int FailedMessages,
    int OptOuts,
    int NeedsHumanReview,
    decimal? MedianFirstResponseMinutes,
    string Methodology);
