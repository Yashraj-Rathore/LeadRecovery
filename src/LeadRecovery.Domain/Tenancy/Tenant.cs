namespace LeadRecovery.Domain.Tenancy;

public sealed class Tenant
{
    private Tenant()
    {
    }

    public Tenant(
        Guid id,
        string name,
        string slug,
        string timezoneId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A tenant ID is required.", nameof(id));
        }

        Id = id;
        Name = NormalizeRequired(name, TenantFieldLimits.NameMaximumLength, nameof(name));
        Slug = NormalizeSlug(slug);
        TimezoneId = NormalizeRequired(
            timezoneId,
            TenantFieldLimits.TimezoneIdMaximumLength,
            nameof(timezoneId));
        Status = TenantStatus.Trial;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string TimezoneId { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; }

    public bool AutomationEnabled { get; private set; }

    public bool DataRetentionEnabled { get; private set; }

    public int DataRetentionDays { get; private set; } =
        TenantFieldLimits.DataRetentionDaysDefault;

    public long Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void UpdateProfile(string name, string timezoneId, DateTimeOffset updatedAtUtc)
    {
        Name = NormalizeRequired(name, TenantFieldLimits.NameMaximumLength, nameof(name));
        TimezoneId = NormalizeRequired(
            timezoneId,
            TenantFieldLimits.TimezoneIdMaximumLength,
            nameof(timezoneId));
        UpdatedAtUtc = RequireCurrentOrLaterUtc(updatedAtUtc);
    }

    public void ChangeStatus(TenantStatus status, DateTimeOffset updatedAtUtc)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (Status == status)
        {
            return;
        }

        Status = status;
        UpdatedAtUtc = RequireCurrentOrLaterUtc(updatedAtUtc);
    }

    public void SetAutomationEnabled(bool enabled, DateTimeOffset updatedAtUtc)
    {
        if (AutomationEnabled == enabled)
        {
            return;
        }

        AutomationEnabled = enabled;
        UpdatedAtUtc = RequireCurrentOrLaterUtc(updatedAtUtc);
    }

    public void ConfigureDataRetention(
        bool enabled,
        int retentionDays,
        DateTimeOffset updatedAtUtc)
    {
        if (retentionDays is < TenantFieldLimits.DataRetentionDaysMinimum or
            > TenantFieldLimits.DataRetentionDaysMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays),
                $"Data retention must be between " +
                $"{TenantFieldLimits.DataRetentionDaysMinimum} and " +
                $"{TenantFieldLimits.DataRetentionDaysMaximum} days.");
        }

        if (DataRetentionEnabled == enabled && DataRetentionDays == retentionDays)
        {
            return;
        }

        DataRetentionEnabled = enabled;
        DataRetentionDays = retentionDays;
        UpdatedAtUtc = RequireCurrentOrLaterUtc(updatedAtUtc);
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeSlug(string slug)
    {
        string normalized = NormalizeRequired(
            slug,
            TenantFieldLimits.SlugMaximumLength,
            nameof(slug)).ToLowerInvariant();

        if (normalized[0] == '-' || normalized[^1] == '-')
        {
            throw new ArgumentException(
                "A tenant slug cannot start or end with a hyphen.",
                nameof(slug));
        }

        foreach (char character in normalized)
        {
            bool isLowercaseAsciiLetter = character is >= 'a' and <= 'z';
            bool isAsciiDigit = character is >= '0' and <= '9';
            if (!isLowercaseAsciiLetter && !isAsciiDigit && character != '-')
            {
                throw new ArgumentException(
                    "A tenant slug can contain only lowercase letters, numbers, and hyphens.",
                    nameof(slug));
            }
        }

        return normalized;
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", parameterName);
        }

        return value;
    }

    private DateTimeOffset RequireCurrentOrLaterUtc(DateTimeOffset value)
    {
        DateTimeOffset utcValue = RequireUtc(value, nameof(value));
        if (utcValue < UpdatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The update timestamp cannot move backwards.");
        }

        return utcValue;
    }
}
