namespace LeadRecovery.Application.Tenancy;

public sealed class TenantContextUnavailableException()
    : InvalidOperationException("A valid server-derived tenant context is required.");
