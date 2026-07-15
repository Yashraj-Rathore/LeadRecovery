namespace LeadRecovery.Infrastructure.Messaging;

public sealed record SmsProviderOptions(
    string Provider,
    bool AllowRealSms,
    string? AccountSid,
    string? AuthToken)
{
    public bool UseTwilio =>
        Provider.Equals("twilio", StringComparison.OrdinalIgnoreCase) &&
        AllowRealSms;
}
