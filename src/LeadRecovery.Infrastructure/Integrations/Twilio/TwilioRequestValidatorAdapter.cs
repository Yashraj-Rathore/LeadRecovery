using LeadRecovery.Application.Integrations;

using Twilio.Security;

namespace LeadRecovery.Infrastructure.Integrations.Twilio;

internal sealed class TwilioRequestValidatorAdapter : ITwilioRequestValidator
{
    private readonly RequestValidator? _validator;

    public TwilioRequestValidatorAdapter(string? authToken)
    {
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            _validator = new RequestValidator(authToken);
        }
    }

    public bool IsConfigured => _validator is not null;

    public bool IsValid(
        string canonicalUrl,
        IReadOnlyDictionary<string, string> formValues,
        string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalUrl);
        ArgumentNullException.ThrowIfNull(formValues);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        return _validator?.Validate(
            canonicalUrl,
            formValues.ToDictionary(pair => pair.Key, pair => pair.Value),
            signature) == true;
    }
}
