using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Common;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Leads;
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

    public DbSet<Lead> Leads => Set<Lead>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<ScheduledAction> ScheduledActions => Set<ScheduledAction>();

    public DbSet<ExternalEventReceipt> ExternalEventReceipts =>
        Set<ExternalEventReceipt>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOwnership<Customer>("customer");
        EnforceTenantOwnership<Lead>("lead");
        EnforceTenantOwnership<Conversation>("conversation");
        EnforceTenantOwnership<Message>("message");
        EnforceTenantOwnership<ScheduledAction>("scheduled action");
        EnforceExternalEventReceiptTenantImmutability();
        ApplyAggregateVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceTenantOwnership<Customer>("customer");
        EnforceTenantOwnership<Lead>("lead");
        EnforceTenantOwnership<Conversation>("conversation");
        EnforceTenantOwnership<Message>("message");
        EnforceTenantOwnership<ScheduledAction>("scheduled action");
        EnforceExternalEventReceiptTenantImmutability();
        ApplyAggregateVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LeadRecoveryDbContext).Assembly);
        modelBuilder.Entity<Customer>()
            .HasQueryFilter(customer => customer.TenantId == ActiveTenantId);
        modelBuilder.Entity<Lead>()
            .HasQueryFilter(lead => lead.TenantId == ActiveTenantId);
        modelBuilder.Entity<Conversation>()
            .HasQueryFilter(conversation => conversation.TenantId == ActiveTenantId);
        modelBuilder.Entity<Message>()
            .HasQueryFilter(message => message.TenantId == ActiveTenantId);
        modelBuilder.Entity<ScheduledAction>()
            .HasQueryFilter(action => action.TenantId == ActiveTenantId);
    }

    private Guid ActiveTenantId => _tenantContext.TenantId;

    private void EnforceTenantOwnership<TEntity>(string entityName)
        where TEntity : class, ITenantOwnedEntity
    {
        foreach (EntityEntry<TEntity> entry in ChangeTracker.Entries<TEntity>())
        {
            if (entry.State is not (
                EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity.TenantId != ActiveTenantId)
            {
                throw new InvalidOperationException(
                    $"A {entityName} cannot be saved outside the active tenant.");
            }

            PropertyEntry tenantId = entry.Property(nameof(ITenantOwnedEntity.TenantId));
            if (entry.State == EntityState.Modified && tenantId.IsModified)
            {
                throw new InvalidOperationException(
                    $"A {entityName}'s tenant ownership cannot be changed.");
            }
        }
    }

    private void ApplyAggregateVersions()
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

        foreach (EntityEntry<Lead> entry in ChangeTracker.Entries<Lead>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            PropertyEntry<Lead, long> version = entry.Property(lead => lead.Version);
            version.CurrentValue = checked(version.OriginalValue + 1);
        }
    }

    private void EnforceExternalEventReceiptTenantImmutability()
    {
        foreach (EntityEntry<ExternalEventReceipt> entry in
            ChangeTracker.Entries<ExternalEventReceipt>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            PropertyEntry<ExternalEventReceipt, Guid?> tenantId =
                entry.Property(receipt => receipt.TenantId);
            if (tenantId.CurrentValue == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "An external event receipt tenant ID cannot be empty.");
            }

            if (entry.State == EntityState.Modified &&
                tenantId.OriginalValue is not null &&
                tenantId.CurrentValue != tenantId.OriginalValue)
            {
                throw new InvalidOperationException(
                    "An external event receipt tenant cannot be changed.");
            }
        }
    }
}
