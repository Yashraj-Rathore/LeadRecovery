using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LeadRecovery.Infrastructure.Persistence;

public sealed class LeadRecoveryDbContext(
    DbContextOptions<LeadRecoveryDbContext> options,
    ITenantContext tenantContext)
    : DbContext(options)
{
    private readonly ITenantContext _tenantContext =
        tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Customer> Customers => Set<Customer>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceCustomerTenantOwnership();
        ApplyTenantVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceCustomerTenantOwnership();
        ApplyTenantVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LeadRecoveryDbContext).Assembly);
        modelBuilder.Entity<Customer>()
            .HasQueryFilter(customer => customer.TenantId == ActiveTenantId);
    }

    private Guid ActiveTenantId => _tenantContext.TenantId;

    private void EnforceCustomerTenantOwnership()
    {
        foreach (EntityEntry<Customer> entry in ChangeTracker.Entries<Customer>())
        {
            if (entry.State is not (
                EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity.TenantId != ActiveTenantId)
            {
                throw new InvalidOperationException(
                    "A customer cannot be saved outside the active tenant.");
            }

            PropertyEntry<Customer, Guid> tenantId =
                entry.Property(customer => customer.TenantId);
            if (entry.State == EntityState.Modified && tenantId.IsModified)
            {
                throw new InvalidOperationException(
                    "A customer's tenant ownership cannot be changed.");
            }
        }
    }

    private void ApplyTenantVersions()
    {
        foreach (EntityEntry<Tenant> entry in ChangeTracker.Entries<Tenant>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            PropertyEntry<Tenant, long> version = entry.Property(tenant => tenant.Version);
            version.CurrentValue = checked(version.OriginalValue + 1);
        }
    }
}
