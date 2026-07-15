using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Tenancy;

public sealed class TenantPhoneNumber : ITenantOwnedEntity
{
    private TenantPhoneNumber()
    {
    }

    public TenantPhoneNumber(
        Guid id,
        Guid tenantId,
        string provider,
        string phoneNumberE164,
        string providerNumberSid,
        IEnumerable<string> recoverableCallStatuses,
        int initialDelaySeconds,
        int recoveryCooldownSeconds,
        bool inboundSmsEnabled = true,
        bool missedCallRecoveryEnabled = true,
        bool isPrimary = false)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        Provider = NormalizeRequired(
            provider,
            TenantPhoneNumberFieldLimits.ProviderMaximumLength,
            nameof(provider));
        PhoneNumberE164 = RequireCanonicalPhone(phoneNumberE164);
        ProviderNumberSid = NormalizeRequired(
            providerNumberSid,
            TenantPhoneNumberFieldLimits.ProviderNumberSidMaximumLength,
            nameof(providerNumberSid));
        RecoverableCallStatuses = NormalizeStatuses(recoverableCallStatuses);
        InitialDelaySeconds = RequireRange(
            initialDelaySeconds,
            0,
            TenantPhoneNumberFieldLimits.InitialDelaySecondsMaximum,
            nameof(initialDelaySeconds));
        RecoveryCooldownSeconds = RequireRange(
            recoveryCooldownSeconds,
            1,
            TenantPhoneNumberFieldLimits.RecoveryCooldownSecondsMaximum,
            nameof(recoveryCooldownSeconds));
        InboundSmsEnabled = inboundSmsEnabled;
        MissedCallRecoveryEnabled = missedCallRecoveryEnabled;
        IsPrimary = isPrimary;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string PhoneNumberE164 { get; private set; } = string.Empty;

    public string ProviderNumberSid { get; private set; } = string.Empty;

    public bool InboundSmsEnabled { get; private set; }

    public bool MissedCallRecoveryEnabled { get; private set; }

    public bool IsPrimary { get; private set; }

    public string[] RecoverableCallStatuses { get; private set; } = [];

    public int InitialDelaySeconds { get; private set; }

    public int RecoveryCooldownSeconds { get; private set; }

    public bool CanRecover(string callStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callStatus);
        string normalized = callStatus.Trim().ToLowerInvariant();
        return RecoverableCallStatuses.Contains(normalized, StringComparer.Ordinal);
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
    }

    private static string NormalizeRequired(
        string? value,
        int maximumLength,
        string parameterName)
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

    private static string RequireCanonicalPhone(string value)
    {
        string normalized = NormalizeRequired(
            value,
            TenantPhoneNumberFieldLimits.PhoneNumberMaximumLength,
            nameof(value));
        if (normalized.Length < 2 || normalized[0] != '+' || normalized[1] is < '1' or > '9')
        {
            throw new ArgumentException(
                "The phone number must be in canonical E.164 format.",
                nameof(value));
        }

        for (int index = 2; index < normalized.Length; index++)
        {
            if (normalized[index] is < '0' or > '9')
            {
                throw new ArgumentException(
                    "The phone number must be in canonical E.164 format.",
                    nameof(value));
            }
        }

        return normalized;
    }

    private static string[] NormalizeStatuses(IEnumerable<string> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        string[] normalized = statuses
            .Select(status => NormalizeCallStatus(status))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "At least one recoverable call status is required.",
                nameof(statuses));
        }

        return normalized;
    }

    private static string NormalizeCallStatus(string value)
    {
        string normalized = NormalizeRequired(
            value,
            TenantPhoneNumberFieldLimits.CallStatusMaximumLength,
            nameof(value)).ToLowerInvariant();
        foreach (char character in normalized)
        {
            if (character is not (>= 'a' and <= 'z') && character != '-')
            {
                throw new ArgumentException(
                    "Call statuses may contain lowercase letters and hyphens only.",
                    nameof(value));
            }
        }

        return normalized;
    }

    private static int RequireRange(
        int value,
        int minimum,
        int maximum,
        string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value must be between {minimum} and {maximum}.");
        }

        return value;
    }
}
