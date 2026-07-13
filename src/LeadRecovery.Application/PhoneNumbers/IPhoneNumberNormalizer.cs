namespace LeadRecovery.Application.PhoneNumbers;

public interface IPhoneNumberNormalizer
{
    PhoneNumberNormalizationResult Normalize(
        string? phoneNumber,
        string? defaultRegion);
}
