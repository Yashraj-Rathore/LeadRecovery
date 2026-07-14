using LeadRecovery.Domain.Identity;

namespace LeadRecovery.Domain.Tests;

public sealed class TenantMembershipTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 14, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorCreatesTenantOwnedMembership()
    {
        Guid id = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();

        TenantMembership membership = new(
            id,
            tenantId,
            userId,
            TenantRole.Owner,
            CreatedAtUtc);

        Assert.Equal(id, membership.Id);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal(userId, membership.UserId);
        Assert.Equal(TenantRole.Owner, membership.Role);
        Assert.Equal(CreatedAtUtc, membership.CreatedAtUtc);
    }

    [Fact]
    public void RoleCanChangeToDefinedTenantRole()
    {
        TenantMembership membership = CreateMembership();

        membership.ChangeRole(TenantRole.Staff);

        Assert.Equal(TenantRole.Staff, membership.Role);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            membership.ChangeRole((TenantRole)99));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ConstructorRejectsEmptyIds(
        bool emptyId,
        bool emptyTenantId,
        bool emptyUserId)
    {
        Assert.Throws<ArgumentException>(() => new TenantMembership(
            emptyId ? Guid.Empty : Guid.CreateVersion7(),
            emptyTenantId ? Guid.Empty : Guid.CreateVersion7(),
            emptyUserId ? Guid.Empty : Guid.CreateVersion7(),
            TenantRole.Staff,
            CreatedAtUtc));
    }

    [Fact]
    public void ConstructorRejectsInvalidRoleAndNonUtcTimestamp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TenantMembership(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            (TenantRole)99,
            CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => new TenantMembership(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            TenantRole.Staff,
            CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
    }

    private static TenantMembership CreateMembership() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            TenantRole.Owner,
            CreatedAtUtc);
}
