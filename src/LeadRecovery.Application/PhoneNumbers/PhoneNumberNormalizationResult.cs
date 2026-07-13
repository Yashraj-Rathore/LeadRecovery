namespace LeadRecovery.Application.PhoneNumbers;

public sealed class PhoneNumberNormalizationResult
{
    private PhoneNumberNormalizationResult(
        string? phoneE164,
        PhoneNumberNormalizationFailure? failure)
    {
        PhoneE164 = phoneE164;
        Failure = failure;
    }

    public bool IsSuccess => PhoneE164 is not null;

    public string? PhoneE164 { get; }

    public PhoneNumberNormalizationFailure? Failure { get; }

    public static PhoneNumberNormalizationResult Success(string phoneE164)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);
        return new PhoneNumberNormalizationResult(phoneE164, null);
    }

    public static PhoneNumberNormalizationResult Failed(
        PhoneNumberNormalizationFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new PhoneNumberNormalizationResult(null, failure);
    }
}
