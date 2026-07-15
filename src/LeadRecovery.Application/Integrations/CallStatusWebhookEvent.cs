namespace LeadRecovery.Application.Integrations;

public sealed record CallStatusWebhookEvent(
    string Provider,
    string CallSid,
    string CallStatus,
    string CallerPhoneE164,
    string DestinationPhoneE164,
    string ExternalEventId,
    string PayloadHash,
    string CorrelationId);
