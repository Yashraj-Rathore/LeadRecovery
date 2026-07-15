using System.Security.Cryptography;
using System.Text;

using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.PhoneNumbers;

namespace LeadRecovery.Api.Integrations.Twilio;

internal sealed class TwilioSmsRequestAdapter(
    TwilioWebhookOptions options,
    ITwilioRequestValidator requestValidator,
    IPhoneNumberNormalizer phoneNumberNormalizer,
    ICallStatusMetrics metrics)
{
    public async Task<TwilioSmsAdapterResult<InboundSmsWebhookEvent>> AdaptInboundAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        SignedFormResult signedForm = await ReadSignedFormAsync(context, cancellationToken);
        if (signedForm.Outcome != TwilioSmsAdapterOutcome.Accepted)
        {
            return new(signedForm.Outcome, null);
        }

        IReadOnlyDictionary<string, string> values = signedForm.Values!;
        if (!TryGetRequired(values, "MessageSid", out string messageSid) ||
            !IsMessageSid(messageSid) ||
            !TryGetRequired(values, "From", out string from) ||
            !TryGetRequired(values, "To", out string to) ||
            !TryGetRequired(values, "Body", out string body) ||
            body.Length > 1600)
        {
            return TwilioSmsAdapterResult<InboundSmsWebhookEvent>.InvalidPayload();
        }

        PhoneNumberNormalizationResult fromResult = phoneNumberNormalizer.Normalize(from, null);
        PhoneNumberNormalizationResult toResult = phoneNumberNormalizer.Normalize(to, null);
        if (!fromResult.IsSuccess || !toResult.IsSuccess)
        {
            return TwilioSmsAdapterResult<InboundSmsWebhookEvent>.InvalidPayload();
        }

        return TwilioSmsAdapterResult<InboundSmsWebhookEvent>.Accepted(
            new InboundSmsWebhookEvent(
                "Twilio",
                messageSid,
                fromResult.PhoneE164!,
                toResult.PhoneE164!,
                body,
                "sha256:" + ComputeSha256(messageSid),
                ComputePayloadHash(values),
                context.TraceIdentifier));
    }

    public async Task<TwilioSmsAdapterResult<DeliveryStatusWebhookEvent>> AdaptStatusAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        SignedFormResult signedForm = await ReadSignedFormAsync(context, cancellationToken);
        if (signedForm.Outcome != TwilioSmsAdapterOutcome.Accepted)
        {
            return new(signedForm.Outcome, null);
        }

        IReadOnlyDictionary<string, string> values = signedForm.Values!;
        if (!TryGetRequired(values, "MessageSid", out string messageSid) ||
            !IsMessageSid(messageSid) ||
            !TryGetRequired(values, "MessageStatus", out string status) ||
            !TryNormalizeStatus(status, out string normalizedStatus))
        {
            return TwilioSmsAdapterResult<DeliveryStatusWebhookEvent>.InvalidPayload();
        }

        values.TryGetValue("ErrorCode", out string? errorCode);
        string normalizedErrorCode = errorCode?.Trim() ?? string.Empty;
        if (normalizedErrorCode.Length > 50)
        {
            return TwilioSmsAdapterResult<DeliveryStatusWebhookEvent>.InvalidPayload();
        }

        return TwilioSmsAdapterResult<DeliveryStatusWebhookEvent>.Accepted(
            new DeliveryStatusWebhookEvent(
                "Twilio",
                messageSid,
                normalizedStatus,
                string.IsNullOrWhiteSpace(normalizedErrorCode) ? null : normalizedErrorCode,
                "sha256:" + ComputeSha256(
                    $"{messageSid}\n{normalizedStatus}\n{normalizedErrorCode}"),
                ComputePayloadHash(values),
                context.TraceIdentifier));
    }

    private async Task<SignedFormResult> ReadSignedFormAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!options.IsConfigured || !requestValidator.IsConfigured)
        {
            return new(TwilioSmsAdapterOutcome.ConfigurationUnavailable, null);
        }

        if (!context.Request.HasFormContentType)
        {
            return new(TwilioSmsAdapterOutcome.InvalidPayload, null);
        }

        IFormCollection form = await context.Request.ReadFormAsync(cancellationToken);
        Dictionary<string, string> values = form.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToString(),
            StringComparer.Ordinal);
        string signature = context.Request.Headers["X-Twilio-Signature"].ToString();
        string canonicalUrl = options.BuildCanonicalUrl(context.Request);
        if (string.IsNullOrWhiteSpace(signature) ||
            !requestValidator.IsValid(canonicalUrl, values, signature))
        {
            metrics.RecordSignatureRejected();
            return new(TwilioSmsAdapterOutcome.InvalidSignature, null);
        }

        return new(TwilioSmsAdapterOutcome.Accepted, values);
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

    private static bool IsMessageSid(string value) =>
        value.Length == 34 &&
        (value.StartsWith("SM", StringComparison.Ordinal) ||
            value.StartsWith("MM", StringComparison.Ordinal)) &&
        value.AsSpan(2).ToString().All(Uri.IsHexDigit);

    private static bool TryNormalizeStatus(string value, out string normalized)
    {
        normalized = value.Trim().ToLowerInvariant();
        return normalized.Length is > 0 and <= 50 &&
            normalized.All(character => character is >= 'a' and <= 'z' or '-');
    }

    private static string ComputePayloadHash(IReadOnlyDictionary<string, string> values) =>
        "sha256:" + ComputeSha256(
            string.Join(
                '\n',
                values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair =>
                        $"{pair.Key.Length}:{pair.Key}{pair.Value.Length}:{pair.Value}")));

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record SignedFormResult(
        TwilioSmsAdapterOutcome Outcome,
        IReadOnlyDictionary<string, string>? Values);
}

internal enum TwilioSmsAdapterOutcome
{
    Accepted,
    ConfigurationUnavailable,
    InvalidSignature,
    InvalidPayload,
}

internal sealed record TwilioSmsAdapterResult<TEvent>(
    TwilioSmsAdapterOutcome Outcome,
    TEvent? WebhookEvent)
    where TEvent : class
{
    public static TwilioSmsAdapterResult<TEvent> Accepted(TEvent webhookEvent) =>
        new(TwilioSmsAdapterOutcome.Accepted, webhookEvent);

    public static TwilioSmsAdapterResult<TEvent> InvalidPayload() =>
        new(TwilioSmsAdapterOutcome.InvalidPayload, null);
}
