using LeadRecovery.Domain.Customers;

namespace LeadRecovery.Domain.Tests;

public sealed class CustomerTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 13, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorCreatesTenantOwnedCustomerWithCanonicalPhone()
    {
        Guid customerId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();

        Customer customer = new(
            customerId,
            tenantId,
            " +14165550123 ",
            CreatedAtUtc,
            " Alex Customer ",
            " alex@example.test ",
            " Toronto ",
            " M5V 2T6 ",
            " Caller initiated contact ");

        Assert.Equal(customerId, customer.Id);
        Assert.Equal(tenantId, customer.TenantId);
        Assert.Equal("+14165550123", customer.PhoneE164);
        Assert.Equal("Alex Customer", customer.Name);
        Assert.Equal("alex@example.test", customer.Email);
        Assert.Equal("Toronto", customer.City);
        Assert.Equal("M5V 2T6", customer.PostalCode);
        Assert.Equal("Caller initiated contact", customer.SmsConsentBasis);
        Assert.Equal(CreatedAtUtc, customer.CreatedAtUtc);
        Assert.Null(customer.OptedOutAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("14165550123")]
    [InlineData("+04165550123")]
    [InlineData("+1 416 555 0123")]
    [InlineData("+1234567890123456")]
    public void ConstructorRejectsNonCanonicalPhone(string phoneNumber)
    {
        Assert.Throws<ArgumentException>(() => new Customer(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            phoneNumber,
            CreatedAtUtc));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ConstructorRejectsEmptyRequiredId(bool emptyCustomerId, bool emptyTenantId)
    {
        Guid customerId = emptyCustomerId ? Guid.Empty : Guid.CreateVersion7();
        Guid tenantId = emptyTenantId ? Guid.Empty : Guid.CreateVersion7();

        Assert.Throws<ArgumentException>(() => new Customer(
            customerId,
            tenantId,
            "+14165550123",
            CreatedAtUtc));
    }

    [Fact]
    public void ConstructorRejectsNonUtcTimestamp()
    {
        Assert.Throws<ArgumentException>(() => new Customer(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "+14165550123",
            CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
    }

    [Fact]
    public void OptOutIsIdempotentAndPreservesFirstTimestamp()
    {
        Customer customer = new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "+14165550123",
            CreatedAtUtc);

        customer.OptOut(CreatedAtUtc.AddMinutes(1));
        customer.OptOut(CreatedAtUtc.AddMinutes(2));

        Assert.Equal(CreatedAtUtc.AddMinutes(1), customer.OptedOutAtUtc);
    }
}
