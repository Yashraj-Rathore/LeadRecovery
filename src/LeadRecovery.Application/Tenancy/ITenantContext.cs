namespace LeadRecovery.Application.Tenancy;

public interface ITenantContext
{
    Guid TenantId { get; }
}
