using LeadRecovery.Domain.Tenancy;

namespace LeadRecovery.Domain.Tests;

public sealed class TenantPhoneNumberTests
{
    [Fact]
    public void ConstructorNormalizesRecoveryPolicy()
    {
        TenantPhoneNumber number = new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            " Twilio ",
            "+14165550100",
            " PN123 ",
            ["FAILED", "no-answer", "busy", "busy"],
            initialDelaySeconds: 30,
            recoveryCooldownSeconds: 300);

        Assert.Equal("Twilio", number.Provider);
        Assert.Equal(["busy", "failed", "no-answer"], number.RecoverableCallStatuses);
        Assert.True(number.CanRecover(" NO-ANSWER "));
        Assert.False(number.CanRecover("completed"));
    }

    [Theory]
    [InlineData(3_601, 300)]
    [InlineData(30, 0)]
    [InlineData(30, 86_401)]
    public void ConstructorRejectsUnsafeTimingPolicy(
        int initialDelaySeconds,
        int recoveryCooldownSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TenantPhoneNumber(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Twilio",
            "+14165550100",
            "PN123",
            ["no-answer"],
            initialDelaySeconds,
            recoveryCooldownSeconds));
    }

    [Fact]
    public void ConstructorRequiresAtLeastOneValidStatus()
    {
        Assert.Throws<ArgumentException>(() => new TenantPhoneNumber(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Twilio",
            "+14165550100",
            "PN123",
            [],
            30,
            300));
    }
}
