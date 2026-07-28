using LeadRecovery.Domain.Tenancy;

namespace LeadRecovery.Domain.Tests;

public sealed class TenantTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 13, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorCreatesSafeTrialTenant()
    {
        Guid tenantId = Guid.CreateVersion7();

        Tenant tenant = new(
            tenantId,
            " Alpha Plumbing ",
            "Alpha-Plumbing",
            " America/Toronto ",
            CreatedAtUtc);

        Assert.Equal(tenantId, tenant.Id);
        Assert.Equal("Alpha Plumbing", tenant.Name);
        Assert.Equal("alpha-plumbing", tenant.Slug);
        Assert.Equal("America/Toronto", tenant.TimezoneId);
        Assert.Equal(TenantStatus.Trial, tenant.Status);
        Assert.False(tenant.AutomationEnabled);
        Assert.False(tenant.DataRetentionEnabled);
        Assert.Equal(TenantFieldLimits.DataRetentionDaysDefault, tenant.DataRetentionDays);
        Assert.Equal(0, tenant.Version);
        Assert.Equal(CreatedAtUtc, tenant.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, tenant.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("-alpha")]
    [InlineData("alpha-")]
    [InlineData("alpha plumbing")]
    [InlineData("alpha_plumbing")]
    public void ConstructorRejectsInvalidSlug(string slug)
    {
        Assert.Throws<ArgumentException>(() => new Tenant(
            Guid.CreateVersion7(),
            "Alpha Plumbing",
            slug,
            "America/Toronto",
            CreatedAtUtc));
    }

    [Fact]
    public void ConstructorRejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(() => new Tenant(
            Guid.Empty,
            "Alpha Plumbing",
            "alpha-plumbing",
            "America/Toronto",
            CreatedAtUtc));
    }

    [Fact]
    public void ConstructorRejectsNonUtcTimestamp()
    {
        DateTimeOffset localTimestamp = CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4));

        Assert.Throws<ArgumentException>(() => new Tenant(
            Guid.CreateVersion7(),
            "Alpha Plumbing",
            "alpha-plumbing",
            "America/Toronto",
            localTimestamp));
    }

    [Fact]
    public void UpdateRejectsTimestampMovingBackwards()
    {
        Tenant tenant = new(
            Guid.CreateVersion7(),
            "Alpha Plumbing",
            "alpha-plumbing",
            "America/Toronto",
            CreatedAtUtc);

        Assert.Throws<ArgumentOutOfRangeException>(() => tenant.UpdateProfile(
            "Updated Plumbing",
            "America/Toronto",
            CreatedAtUtc.AddSeconds(-1)));
    }

    [Fact]
    public void AutomationSwitchIsExplicitIdempotentAndMonotonic()
    {
        Tenant tenant = new(
            Guid.CreateVersion7(),
            "Alpha Plumbing",
            "alpha-plumbing",
            "America/Toronto",
            CreatedAtUtc);

        tenant.SetAutomationEnabled(true, CreatedAtUtc.AddMinutes(1));
        tenant.SetAutomationEnabled(true, CreatedAtUtc);

        Assert.True(tenant.AutomationEnabled);
        Assert.Equal(CreatedAtUtc.AddMinutes(1), tenant.UpdatedAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tenant.SetAutomationEnabled(false, CreatedAtUtc));
    }

    [Fact]
    public void DataRetentionPolicyIsOptInBoundedAndMonotonic()
    {
        Tenant tenant = new(
            Guid.CreateVersion7(),
            "Alpha Plumbing",
            "alpha-plumbing",
            "America/Toronto",
            CreatedAtUtc);

        tenant.ConfigureDataRetention(true, 180, CreatedAtUtc.AddMinutes(1));
        tenant.ConfigureDataRetention(true, 180, CreatedAtUtc);

        Assert.True(tenant.DataRetentionEnabled);
        Assert.Equal(180, tenant.DataRetentionDays);
        Assert.Equal(CreatedAtUtc.AddMinutes(1), tenant.UpdatedAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tenant.ConfigureDataRetention(true, 29, CreatedAtUtc.AddMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tenant.ConfigureDataRetention(
                true,
                3_651,
                CreatedAtUtc.AddMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tenant.ConfigureDataRetention(false, 365, CreatedAtUtc));
    }
}
