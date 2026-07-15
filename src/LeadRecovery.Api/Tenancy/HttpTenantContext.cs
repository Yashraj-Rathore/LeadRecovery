using LeadRecovery.Application.Tenancy;

namespace LeadRecovery.Api.Tenancy;

internal sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    : ITenantContext, ITenantExecutionScope
{
    private Guid? _trustedTenantId;

    public Guid TenantId
    {
        get
        {
            if (_trustedTenantId is Guid trustedTenantId)
            {
                return trustedTenantId;
            }

            string? tenantIdValue = httpContextAccessor.HttpContext?.User
                .FindFirst(TenantClaimTypes.TenantId)?.Value;

            if (!Guid.TryParse(tenantIdValue, out Guid tenantId) || tenantId == Guid.Empty)
            {
                throw new TenantContextUnavailableException();
            }

            return tenantId;
        }
    }

    public IDisposable Begin(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty tenant ID is required.", nameof(tenantId));
        }

        if (_trustedTenantId is not null)
        {
            throw new InvalidOperationException("A trusted tenant scope is already active.");
        }

        _trustedTenantId = tenantId;
        return new TrustedTenantScope(this);
    }

    private sealed class TrustedTenantScope(HttpTenantContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            owner._trustedTenantId = null;
            _disposed = true;
        }
    }
}
