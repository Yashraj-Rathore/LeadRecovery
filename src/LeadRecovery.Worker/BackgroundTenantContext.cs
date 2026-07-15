using LeadRecovery.Application.Tenancy;

namespace LeadRecovery.Worker;

internal sealed class BackgroundTenantContext : ITenantContext, ITenantExecutionScope
{
    private Guid? _tenantId;

    public Guid TenantId => _tenantId ?? throw new TenantContextUnavailableException();

    public IDisposable Begin(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty tenant ID is required.", nameof(tenantId));
        }

        if (_tenantId is not null)
        {
            throw new InvalidOperationException("A background tenant scope is already active.");
        }

        _tenantId = tenantId;
        return new Scope(this);
    }

    private sealed class Scope(BackgroundTenantContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            owner._tenantId = null;
            _disposed = true;
        }
    }
}
