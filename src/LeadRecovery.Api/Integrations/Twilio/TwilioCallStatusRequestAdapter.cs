using System.Security.Cryptography;
using System.Text;

using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.PhoneNumbers;

namespace LeadRecovery.Api.Integrations.Twilio;

internal sealed class TwilioCallStatusRequestAdapter(
    TwilioWebhookOptions options,
    ITwilioRequestValidator requestValidator,
    IPhoneNumberNormalizer phoneNumberNormalizer,
    ICallStatusMetrics metrics)
{
    public async Task<TwilioCallStatusAdapterResult> AdaptAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!options.IsConfigured || !requestValidator.IsConfigured)
        {
            return TwilioCallStatusAdapterResult.ConfigurationUnavailable();
        }

        if (!context.Request.HasFormContentType)
        {
            return TwilioCallStatusAdapterResult.InvalidPayload();
        }

        IFormCollection form = await context.Request.ReadFormAsync(cancellationToken);
        Dictionary<string, string> formValues = form.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToString(),
            StringComparer.Ordinal);
        string signature = context.Request.Headers["X-Twilio-Signature"].ToString();
        string canonicalUrl = options.BuildCanonicalUrl(context.Request);
        if (string.IsNullOrWhiteSpace(signature) ||
            !requestValidator.IsValid(canonicalUrl, formValues, signature))
        {
            metrics.RecordSignatureRejected();
            return TwilioCallStatusAdapterResult.InvalidSignature();
        }

        if (!TryGetRequired(formValues, "CallSid", out string callSid) ||
            !IsCallSid(callSid) ||
            !TryGetRequired(formValues, "CallStatus", out string callStatus) ||
            !TryNormalizeStatus(callStatus, out string normalizedStatus) ||
            !TryGetPhone(formValues, "From", "Caller", out string caller) ||
            !TryGetPhone(formValues, "To", "Called", out string destination))
        {
            return TwilioCallStatusAdapterResult.InvalidPayload();
        }

        PhoneNumberNormalizationResult callerResult =
            phoneNumberNormalizer.Normalize(caller, null);
        PhoneNumberNormalizationResult destinationResult =
            phoneNumberNormalizer.Normalize(destination, null);
        if (!callerResult.IsSuccess || !destinationResult.IsSuccess)
        {
            return TwilioCallStatusAdapterResult.InvalidPayload();
        }

        string externalEventId = "sha256:" + ComputeSha256(
            $"{callSid}\n{normalizedStatus}");
        string payloadHash = "sha256:" + ComputeSha256(
            string.Join(
                '\n',
                formValues
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key.Length}:{pair.Key}{pair.Value.Length}:{pair.Value}")));
        return TwilioCallStatusAdapterResult.Accepted(new CallStatusWebhookEvent(
            "Twilio",
            callSid,
            normalizedStatus,
            callerResult.PhoneE164!,
            destinationResult.PhoneE164!,
            externalEventId,
            payloadHash,
            context.TraceIdentifier));
    }

    private static bool TryGetRequired(
        IReadOnlyDictionary<string, string> values,
        string key,
        out string value)
    {
        if (values.TryGetValue(key, out string? candidate) &&
            !string.IsNullOrWhiteSpace(candidate))
        {
            value = candidate.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetPhone(
        IReadOnlyDictionary<string, string> values,
        string primaryKey,
        string fallbackKey,
        out string value) =>
        TryGetRequired(values, primaryKey, out value) ||
        TryGetRequired(values, fallbackKey, out value);

    private static bool IsCallSid(string value)
    {
        if (value.Length != 34 || !value.StartsWith("CA", StringComparison.Ordinal))
        {
            return false;
        }

        return value.AsSpan(2).ToString().All(Uri.IsHexDigit);
    }

    private static bool TryNormalizeStatus(string value, out string normalized)
    {
        normalized = value.Trim().ToLowerInvariant();
        return normalized.Length is > 0 and <= 50 &&
            normalized.All(character => character is >= 'a' and <= 'z' or '-');
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

internal enum TwilioCallStatusAdapterOutcome
{
    Accepted,
    ConfigurationUnavailable,
    InvalidSignature,
    InvalidPayload,
}

internal sealed record TwilioCallStatusAdapterResult(
    TwilioCallStatusAdapterOutcome Outcome,
    CallStatusWebhookEvent? WebhookEvent)
{
    public static TwilioCallStatusAdapterResult Accepted(
        CallStatusWebhookEvent webhookEvent) =>
        new(TwilioCallStatusAdapterOutcome.Accepted, webhookEvent);

    public static TwilioCallStatusAdapterResult ConfigurationUnavailable() =>
        new(TwilioCallStatusAdapterOutcome.ConfigurationUnavailable, null);

    public static TwilioCallStatusAdapterResult InvalidSignature() =>
        new(TwilioCallStatusAdapterOutcome.InvalidSignature, null);

    public static TwilioCallStatusAdapterResult InvalidPayload() =>
        new(TwilioCallStatusAdapterOutcome.InvalidPayload, null);
}
