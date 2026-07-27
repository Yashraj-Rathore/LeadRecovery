namespace LeadRecovery.Application.Messaging;

public sealed record SmsSendRequest(
    Guid TenantId,
    string FromPhoneE164,
    string ToPhoneE164,
    string Body,
    string IdempotencyKey,
    Uri StatusCallbackUri);

public enum SmsSendDisposition
{
    Accepted,
    TransientFailure,
    PermanentFailure,
}

public sealed record SmsSendResult(
    SmsSendDisposition Disposition,
    string? ProviderMessageSid,
    string? FailureCode,
    string? FailureDescription)
{
    public static SmsSendResult Accepted(string providerMessageSid) =>
        new(SmsSendDisposition.Accepted, providerMessageSid, null, null);

    public static SmsSendResult Transient(string code, string description) =>
        new(SmsSendDisposition.TransientFailure, null, code, description);

    public static SmsSendResult Permanent(string code, string description) =>
        new(SmsSendDisposition.PermanentFailure, null, code, description);
}

public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(
        SmsSendRequest request,
        CancellationToken cancellationToken);
}

public sealed record PreparedOutboundSms(
    Guid TenantId,
    Guid ActionId,
    Guid MessageId,
    SmsSendRequest Request);

public enum OutboundSmsOutcome
{
    Ignored,
    Suppressed,
    Accepted,
    RetryScheduled,
    PermanentlyFailed,
}

public sealed record InboundSmsWebhookEvent(
    string Provider,
    string ProviderMessageSid,
    string FromPhoneE164,
    string ToPhoneE164,
    string Body,
    string ExternalEventId,
    string PayloadHash,
    string CorrelationId);

public enum InboundSmsOutcome
{
    Received,
    OptedOut,
    Duplicate,
    IgnoredUnknownNumber,
    IgnoredTenantInactive,
}

public sealed record DeliveryStatusWebhookEvent(
    string Provider,
    string ProviderMessageSid,
    string Status,
    string? ErrorCode,
    string ExternalEventId,
    string PayloadHash,
    string CorrelationId);

public enum DeliveryStatusOutcome
{
    Updated,
    Duplicate,
    IgnoredUnknownMessage,
    IgnoredStatus,
}

public interface ISmsWorkflowPersistence
{
    Task<PreparedOutboundSms?> PrepareOutboundAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        DateTimeOffset now,
        Uri statusCallbackUri,
        CancellationToken cancellationToken);

    Task<OutboundSmsOutcome> CompleteOutboundAsync(
        PreparedOutboundSms prepared,
        SmsSendResult result,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<InboundSmsOutcome> ProcessInboundAsync(
        InboundSmsWebhookEvent webhookEvent,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<DeliveryStatusOutcome> ProcessDeliveryStatusAsync(
        DeliveryStatusWebhookEvent webhookEvent,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IManualSmsWorkflowPersistence
{
    Task<PreparedOutboundSms?> PrepareManualOutboundAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        DateTimeOffset now,
        Uri statusCallbackUri,
        CancellationToken cancellationToken);

    Task<OutboundSmsOutcome> CompleteManualOutboundAsync(
        PreparedOutboundSms prepared,
        SmsSendResult result,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IWorkflowSmsPersistence
{
    Task<PreparedOutboundSms?> PrepareWorkflowOutboundAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        DateTimeOffset now,
        Uri statusCallbackUri,
        CancellationToken cancellationToken);

    Task<OutboundSmsOutcome> CompleteWorkflowOutboundAsync(
        PreparedOutboundSms prepared,
        SmsSendResult result,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public static class SmsScheduledActionTypes
{
    public const string SendManualSms = "SendManualSms";
}

public static class SmsTemplatePurposes
{
    public const string InitialMissedCallRecovery = "InitialMissedCallRecovery";
    public const string BookingLink = "BookingLink";
}

public interface ISmsMetrics
{
    void RecordOutbound(OutboundSmsOutcome outcome);

    void RecordInbound(InboundSmsOutcome outcome);

    void RecordDelivery(DeliveryStatusOutcome outcome);
}
