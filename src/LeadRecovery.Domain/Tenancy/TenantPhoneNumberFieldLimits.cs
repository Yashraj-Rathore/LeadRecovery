namespace LeadRecovery.Domain.Tenancy;

public static class TenantPhoneNumberFieldLimits
{
    public const int ProviderMaximumLength = 50;
    public const int PhoneNumberMaximumLength = 16;
    public const int ProviderNumberSidMaximumLength = 100;
    public const int CallStatusMaximumLength = 50;
    public const int InitialDelaySecondsMaximum = 3_600;
    public const int RecoveryCooldownSecondsMaximum = 86_400;
}
