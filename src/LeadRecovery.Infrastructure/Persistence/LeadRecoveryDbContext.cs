using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Common;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LeadRecovery.Infrastructure.Persistence;

public sealed class LeadRecoveryDbContext(
    DbContextOptions<LeadRecoveryDbContext> options,
    ITenantContext tenantContext)
    : IdentityUserContext<ApplicationUser, Guid>(options)
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

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOwnership<Customer>("customer");
        EnforceTenantOwnership<Lead>("lead");
        EnforceTenantOwnership<Conversation>("conversation");
        EnforceTenantOwnership<Message>("message");
        EnforceTenantOwnership<ScheduledAction>("scheduled action");
        EnforceTenantOwnership<TenantMembership>("tenant membership");
        EnforceExternalEventReceiptTenantImmutability();
        EnforceAuditEventTenantImmutability();
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
        EnforceTenantOwnership<TenantMembership>("tenant membership");
        EnforceExternalEventReceiptTenantImmutability();
        EnforceAuditEventTenantImmutability();
        ApplyAggregateVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        ConfigureIdentityTables(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(LeadRecoveryDbContext).Assembly);
        builder.Entity<Customer>()
            .HasQueryFilter(customer => customer.TenantId == ActiveTenantId);
        builder.Entity<Lead>()
            .HasQueryFilter(lead => lead.TenantId == ActiveTenantId);
        builder.Entity<Conversation>()
            .HasQueryFilter(conversation => conversation.TenantId == ActiveTenantId);
        builder.Entity<Message>()
            .HasQueryFilter(message => message.TenantId == ActiveTenantId);
        builder.Entity<ScheduledAction>()
            .HasQueryFilter(action => action.TenantId == ActiveTenantId);
        builder.Entity<TenantMembership>()
            .HasQueryFilter(membership => membership.TenantId == ActiveTenantId);
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

    private void EnforceAuditEventTenantImmutability()
    {
        foreach (EntityEntry<AuditEvent> entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            PropertyEntry<AuditEvent, Guid?> tenantId =
                entry.Property(auditEvent => auditEvent.TenantId);
            if (tenantId.CurrentValue == Guid.Empty)
            {
                throw new InvalidOperationException("An audit-event tenant ID cannot be empty.");
            }

            if (entry.State == EntityState.Modified && tenantId.IsModified)
            {
                throw new InvalidOperationException("An audit-event tenant cannot be changed.");
            }
        }
    }

    private static void ConfigureIdentityTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserClaim<Guid>>(builder =>
        {
            builder.ToTable("user_claims");
            builder.HasKey(claim => claim.Id).HasName("pk_user_claims");
            builder.Property(claim => claim.Id).HasColumnName("id");
            builder.Property(claim => claim.UserId).HasColumnName("user_id");
            builder.Property(claim => claim.ClaimType)
                .HasColumnName("claim_type")
                .HasMaxLength(256);
            builder.Property(claim => claim.ClaimValue)
                .HasColumnName("claim_value")
                .HasMaxLength(1024);
            builder.HasIndex(claim => claim.UserId)
                .HasDatabaseName("ix_user_claims_user_id");
            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(claim => claim.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_claims_users_user_id");
        });
        modelBuilder.Entity<IdentityUserLogin<Guid>>(builder =>
        {
            builder.ToTable("user_logins");
            builder.HasKey(login => new { login.LoginProvider, login.ProviderKey })
                .HasName("pk_user_logins");
            builder.Property(login => login.LoginProvider)
                .HasColumnName("login_provider")
                .HasMaxLength(128);
            builder.Property(login => login.ProviderKey)
                .HasColumnName("provider_key")
                .HasMaxLength(128);
            builder.Property(login => login.ProviderDisplayName)
                .HasColumnName("provider_display_name")
                .HasMaxLength(256);
            builder.Property(login => login.UserId).HasColumnName("user_id");
            builder.HasIndex(login => login.UserId)
                .HasDatabaseName("ix_user_logins_user_id");
            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(login => login.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_logins_users_user_id");
        });
        modelBuilder.Entity<IdentityUserToken<Guid>>(builder =>
        {
            builder.ToTable("user_tokens");
            builder.HasKey(token => new
            {
                token.UserId,
                token.LoginProvider,
                token.Name,
            })
                .HasName("pk_user_tokens");
            builder.Property(token => token.UserId).HasColumnName("user_id");
            builder.Property(token => token.LoginProvider)
                .HasColumnName("login_provider")
                .HasMaxLength(128);
            builder.Property(token => token.Name)
                .HasColumnName("name")
                .HasMaxLength(128);
            builder.Property(token => token.Value).HasColumnName("value");
            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_tokens_users_user_id");
        });
    }
}
