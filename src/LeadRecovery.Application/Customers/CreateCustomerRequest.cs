namespace LeadRecovery.Application.Customers;

public sealed record CreateCustomerRequest(
    string? PhoneNumber,
    string? DefaultRegion,
    DateTimeOffset CreatedAtUtc,
    string? Name = null,
    string? Email = null,
    string? City = null,
    string? PostalCode = null,
    string? SmsConsentBasis = null);
