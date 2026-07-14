using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Identity;

public sealed class TenantMembership : ITenantOwnedEntity
{
    private TenantMembership()
    {
    }

    public TenantMembership(
        Guid id,
        Guid tenantId,
        Guid userId,
        TenantRole role,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        UserId = RequireId(userId, nameof(userId));
        Role = RequireDefined(role, nameof(role));
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public TenantRole Role { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void ChangeRole(TenantRole role)
    {
        Role = RequireDefined(role, nameof(role));
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
    }

    private static TenantRole RequireDefined(TenantRole role, string parameterName)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return role;
    }

    private static DateTimeOffset RequireUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", parameterName);
        }

        return value;
    }
}
