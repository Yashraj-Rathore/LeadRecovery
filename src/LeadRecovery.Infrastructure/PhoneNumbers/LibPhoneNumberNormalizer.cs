using LeadRecovery.Application.PhoneNumbers;

using PhoneNumbers;

namespace LeadRecovery.Infrastructure.PhoneNumbers;

public sealed class LibPhoneNumberNormalizer : IPhoneNumberNormalizer
{
    private readonly PhoneNumberUtil _phoneNumberUtil = PhoneNumberUtil.GetInstance();

    public PhoneNumberNormalizationResult Normalize(
        string? phoneNumber,
        string? defaultRegion)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return PhoneNumberNormalizationResult.Failed(
                PhoneNumberNormalizationFailure.MissingInput);
        }

        string rawPhoneNumber = phoneNumber.Trim();
        string? region = null;
        if (!rawPhoneNumber.StartsWith('+'))
        {
            if (string.IsNullOrWhiteSpace(defaultRegion))
            {
                return PhoneNumberNormalizationResult.Failed(
                    PhoneNumberNormalizationFailure.MissingDefaultRegion);
            }

            region = defaultRegion.Trim().ToUpperInvariant();
            if (region.Length != 2 || !_phoneNumberUtil.GetSupportedRegions().Contains(region))
            {
                return PhoneNumberNormalizationResult.Failed(
                    PhoneNumberNormalizationFailure.UnsupportedRegion);
            }
        }

        PhoneNumber parsedNumber;
        try
        {
            parsedNumber = _phoneNumberUtil.Parse(rawPhoneNumber, region);
        }
        catch (NumberParseException)
        {
            return PhoneNumberNormalizationResult.Failed(
                PhoneNumberNormalizationFailure.ParseFailed);
        }

        if (!_phoneNumberUtil.IsPossibleNumber(parsedNumber))
        {
            return PhoneNumberNormalizationResult.Failed(
                PhoneNumberNormalizationFailure.NotPossible);
        }

        if (!_phoneNumberUtil.IsValidNumber(parsedNumber))
        {
            return PhoneNumberNormalizationResult.Failed(
                PhoneNumberNormalizationFailure.Invalid);
        }

        return PhoneNumberNormalizationResult.Success(
            _phoneNumberUtil.Format(parsedNumber, PhoneNumberFormat.E164));
    }
}
