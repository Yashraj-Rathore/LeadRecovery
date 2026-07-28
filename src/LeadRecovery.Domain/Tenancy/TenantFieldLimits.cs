namespace LeadRecovery.Domain.Tenancy;

public static class TenantFieldLimits
{
    public const int NameMaximumLength = 200;
    public const int SlugMaximumLength = 100;
    public const int TimezoneIdMaximumLength = 100;
    public const int DataRetentionDaysMinimum = 30;
    public const int DataRetentionDaysMaximum = 3_650;
    public const int DataRetentionDaysDefault = 365;
}
