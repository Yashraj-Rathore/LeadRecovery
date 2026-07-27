namespace LeadRecovery.Infrastructure.Analysis;

public sealed record OpenAiLeadAnalysisOptions
{
    public const string ProviderName = "OpenAI";
    public const string DefaultModel = "gpt-5.6-sol";
    public const int DefaultMaximumRetryCount = 2;
    public const int DefaultMaximumOutputTokens = 1_000;

    public static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/responses");
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultRetryBaseDelay = TimeSpan.FromMilliseconds(250);

    public OpenAiLeadAnalysisOptions(
        string apiKey,
        string model,
        TimeSpan? timeout = null,
        int maximumRetryCount = DefaultMaximumRetryCount,
        TimeSpan? retryBaseDelay = null,
        int maximumOutputTokens = DefaultMaximumOutputTokens,
        Uri? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        TimeSpan resolvedTimeout = timeout ?? DefaultTimeout;
        TimeSpan resolvedRetryBaseDelay = retryBaseDelay ?? DefaultRetryBaseDelay;
        Uri resolvedEndpoint = endpoint ?? DefaultEndpoint;
        if (apiKey.Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(apiKey));
        }

        if (model.Length > 100 || !model.All(IsModelCharacter))
        {
            throw new ArgumentException(
                "AI_MODEL may contain only letters, digits, periods, underscores, and hyphens and cannot exceed 100 characters.",
                nameof(model));
        }

        if (resolvedTimeout < TimeSpan.FromSeconds(1) ||
            resolvedTimeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "AI_TIMEOUT_SECONDS must be between 1 and 30.");
        }

        if (maximumRetryCount is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRetryCount),
                "AI_MAX_RETRIES must be between 0 and 2.");
        }

        if (resolvedRetryBaseDelay < TimeSpan.FromMilliseconds(50) ||
            resolvedRetryBaseDelay > TimeSpan.FromSeconds(2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryBaseDelay),
                "AI_RETRY_BASE_DELAY_MILLISECONDS must be between 50 and 2000.");
        }

        if (maximumOutputTokens is < 256 or > 2_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOutputTokens),
                "AI_MAX_OUTPUT_TOKENS must be between 256 and 2000.");
        }

        if (!resolvedEndpoint.IsAbsoluteUri ||
            !resolvedEndpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(resolvedEndpoint.UserInfo) ||
            !string.IsNullOrEmpty(resolvedEndpoint.Query) ||
            !string.IsNullOrEmpty(resolvedEndpoint.Fragment))
        {
            throw new ArgumentException(
                "The OpenAI Responses API endpoint must be an absolute HTTPS URI without credentials, query, or fragment.",
                nameof(endpoint));
        }

        ApiKey = apiKey;
        Model = model;
        Timeout = resolvedTimeout;
        MaximumRetryCount = maximumRetryCount;
        RetryBaseDelay = resolvedRetryBaseDelay;
        MaximumOutputTokens = maximumOutputTokens;
        Endpoint = resolvedEndpoint;
    }

    public string ApiKey { get; }

    public string Model { get; }

    public TimeSpan Timeout { get; }

    public int MaximumRetryCount { get; }

    public TimeSpan RetryBaseDelay { get; }

    public int MaximumOutputTokens { get; }

    public Uri Endpoint { get; }

    private static bool IsModelCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';
}
