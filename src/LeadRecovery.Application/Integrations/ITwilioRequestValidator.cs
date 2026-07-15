namespace LeadRecovery.Application.Integrations;

public interface ITwilioRequestValidator
{
    bool IsConfigured { get; }

    bool IsValid(
        string canonicalUrl,
        IReadOnlyDictionary<string, string> formValues,
        string signature);
}
