using LeadRecovery.Application.Tenancy;

namespace LeadRecovery.Api.Tenancy;

internal sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            string? tenantIdValue = httpContextAccessor.HttpContext?.User
                .FindFirst(TenantClaimTypes.TenantId)?.Value;

            if (!Guid.TryParse(tenantIdValue, out Guid tenantId) || tenantId == Guid.Empty)
            {
                throw new TenantContextUnavailableException();
            }

            return tenantId;
        }
    }
}
