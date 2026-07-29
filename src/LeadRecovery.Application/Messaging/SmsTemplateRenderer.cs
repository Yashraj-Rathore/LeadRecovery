using LeadRecovery.Domain.Conversations;

namespace LeadRecovery.Application.Messaging;

public static class SmsTemplateRenderer
{
    public const string BusinessNameToken = "{{BusinessName}}";
    public const string BookingUrlToken = "{{BookingUrl}}";

    public static SmsTemplateRenderResult Render(
        string? templateBody,
        string? businessName,
        string? bookingUrl)
    {
        if (string.IsNullOrWhiteSpace(templateBody))
        {
            return SmsTemplateRenderResult.Invalid("The template body is required.");
        }

        if (string.IsNullOrWhiteSpace(businessName))
        {
            return SmsTemplateRenderResult.Invalid(
                "A business name is required to render the template.");
        }

        string? unsupportedToken = FindUnsupportedToken(templateBody);
        if (unsupportedToken is not null)
        {
            return SmsTemplateRenderResult.Invalid(
                $"The template contains unsupported placeholder '{unsupportedToken}'.");
        }

        if (templateBody.Contains(BookingUrlToken, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(bookingUrl))
        {
            return SmsTemplateRenderResult.Invalid(
                "A booking URL is required when the template uses {{BookingUrl}}.");
        }

        string renderedBody = templateBody.Replace(
            BusinessNameToken,
            businessName,
            StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(bookingUrl))
        {
            renderedBody = renderedBody.Replace(
                BookingUrlToken,
                bookingUrl,
                StringComparison.Ordinal);
        }

        if (renderedBody.Length > MessageFieldLimits.BodyMaximumLength)
        {
            return SmsTemplateRenderResult.Invalid(
                $"The rendered SMS body cannot exceed " +
                $"{MessageFieldLimits.BodyMaximumLength} characters.");
        }

        return SmsTemplateRenderResult.Valid(renderedBody);
    }

    private static string? FindUnsupportedToken(string templateBody)
    {
        int searchFrom = 0;
        while (searchFrom < templateBody.Length)
        {
            int start = templateBody.IndexOf("{{", searchFrom, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            int end = templateBody.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                return templateBody[start..];
            }

            string token = templateBody[start..(end + 2)];
            if (token is not (BusinessNameToken or BookingUrlToken))
            {
                return token;
            }

            searchFrom = end + 2;
        }

        return null;
    }
}

public sealed record SmsTemplateRenderResult(string? Body, string? Error)
{
    public bool IsValid => Body is not null && Error is null;

    public static SmsTemplateRenderResult Valid(string body) => new(body, null);

    public static SmsTemplateRenderResult Invalid(string error) => new(null, error);
}
