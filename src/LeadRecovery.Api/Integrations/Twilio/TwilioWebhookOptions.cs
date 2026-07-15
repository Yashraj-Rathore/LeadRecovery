namespace LeadRecovery.Api.Integrations.Twilio;

internal sealed class TwilioWebhookOptions
{
    public TwilioWebhookOptions(string? canonicalBaseUrl, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(canonicalBaseUrl))
        {
            return;
        }

        if (!Uri.TryCreate(canonicalBaseUrl.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "TWILIO_WEBHOOK_BASE_URL must be an absolute HTTP(S) URL " +
                "without credentials, a query string, or a fragment.");
        }

        if (!isDevelopment && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "TWILIO_WEBHOOK_BASE_URL must use HTTPS outside Development.");
        }

        CanonicalBaseUri = uri;
    }

    public Uri? CanonicalBaseUri { get; }

    public bool IsConfigured => CanonicalBaseUri is not null;

    public string BuildCanonicalUrl(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Uri baseUri = CanonicalBaseUri ?? throw new InvalidOperationException(
            "Twilio webhook canonical URL configuration is unavailable.");
        UriBuilder builder = new(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}{request.PathBase}{request.Path}",
            Query = request.QueryString.HasValue
                ? request.QueryString.Value![1..]
                : string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }
}
