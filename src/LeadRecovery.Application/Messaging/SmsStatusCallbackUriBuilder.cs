namespace LeadRecovery.Application.Messaging;

public static class SmsStatusCallbackUriBuilder
{
    private const string StatusCallbackPath =
        "api/v1/webhooks/twilio/sms/status";

    public static Uri Build(string? webhookBaseUrl, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(webhookBaseUrl) ||
            !Uri.TryCreate(webhookBaseUrl.Trim(), UriKind.Absolute, out Uri? baseUri) ||
            baseUri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException(
                "TWILIO_WEBHOOK_BASE_URL must be an absolute HTTP(S) URL " +
                "without credentials, a query string, or a fragment.");
        }

        if (!isDevelopment && baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "TWILIO_WEBHOOK_BASE_URL must use HTTPS outside Development.");
        }

        UriBuilder callback = new(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/{StatusCallbackPath}",
        };
        return callback.Uri;
    }
}
