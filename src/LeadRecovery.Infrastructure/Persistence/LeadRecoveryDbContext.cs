using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LeadRecovery.Infrastructure.Persistence;

public sealed class LeadRecoveryDbContext(DbContextOptions<LeadRecoveryDbContext> options)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LeadRecoveryDbContext).Assembly);
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
