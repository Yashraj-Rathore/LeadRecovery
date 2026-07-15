namespace LeadRecovery.Application.Tenancy;

public interface ITenantExecutionScope
{
    IDisposable Begin(Guid tenantId);
}
