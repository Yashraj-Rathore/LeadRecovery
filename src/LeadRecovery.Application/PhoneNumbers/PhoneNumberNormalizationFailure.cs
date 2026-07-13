namespace LeadRecovery.Application.PhoneNumbers;

public enum PhoneNumberNormalizationFailure
{
    MissingInput,
    MissingDefaultRegion,
    UnsupportedRegion,
    ParseFailed,
    NotPossible,
    Invalid,
}
