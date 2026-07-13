namespace LeadRecovery.Domain.Customers;

public sealed class Customer
{
    private Customer()
    {
    }

    public Customer(
        Guid id,
        Guid tenantId,
        string phoneE164,
        DateTimeOffset createdAtUtc,
        string? name = null,
        string? email = null,
        string? city = null,
        string? postalCode = null,
        string? smsConsentBasis = null)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        PhoneE164 = RequireCanonicalPhone(phoneE164);
        Name = NormalizeOptional(name, CustomerFieldLimits.NameMaximumLength, nameof(name));
        Email = NormalizeOptional(email, CustomerFieldLimits.EmailMaximumLength, nameof(email));
        City = NormalizeOptional(city, CustomerFieldLimits.CityMaximumLength, nameof(city));
        PostalCode = NormalizeOptional(
            postalCode,
            CustomerFieldLimits.PostalCodeMaximumLength,
            nameof(postalCode));
        SmsConsentBasis = NormalizeOptional(
            smsConsentBasis,
            CustomerFieldLimits.SmsConsentBasisMaximumLength,
            nameof(smsConsentBasis));
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string PhoneE164 { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public string? Email { get; private set; }

    public string? City { get; private set; }

    public string? PostalCode { get; private set; }

    public string? SmsConsentBasis { get; private set; }

    public DateTimeOffset? OptedOutAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
    }

    private static string RequireCanonicalPhone(string phoneE164)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);

        string normalized = phoneE164.Trim();
        if (normalized.Length is < 2 or > CustomerFieldLimits.PhoneE164MaximumLength ||
            normalized[0] != '+' ||
            normalized[1] is < '1' or > '9')
        {
            throw new ArgumentException(
                "The phone number must be in canonical E.164 format.",
                nameof(phoneE164));
        }

        for (int index = 2; index < normalized.Length; index++)
        {
            if (normalized[index] is < '0' or > '9')
            {
                throw new ArgumentException(
                    "The phone number must be in canonical E.164 format.",
                    nameof(phoneE164));
            }
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
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
}
