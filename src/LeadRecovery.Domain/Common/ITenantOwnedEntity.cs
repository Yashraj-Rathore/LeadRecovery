namespace LeadRecovery.Domain.Common;

public interface ITenantOwnedEntity
{
    Guid TenantId { get; }
}
