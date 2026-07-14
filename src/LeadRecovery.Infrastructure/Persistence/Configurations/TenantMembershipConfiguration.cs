using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class TenantMembershipConfiguration
    : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "tenant_memberships",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_tenant_memberships_role",
                "role in ('Owner', 'Manager', 'Staff', 'ReadOnly')"));
        builder.HasKey(membership => membership.Id).HasName("pk_tenant_memberships");
        builder.HasAlternateKey(membership => new { membership.TenantId, membership.Id })
            .HasName("ak_tenant_memberships_tenant_id_id");
        builder.HasAlternateKey(membership => new { membership.TenantId, membership.UserId })
            .HasName("ak_tenant_memberships_tenant_id_user_id");
        builder.HasIndex(membership => membership.UserId)
            .HasDatabaseName("ix_tenant_memberships_user_id");

        builder.Property(membership => membership.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(membership => membership.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(membership => membership.UserId)
            .HasColumnName("user_id")
            .IsRequired();
        builder.Property(membership => membership.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(membership => membership.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(membership => membership.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tenant_memberships_tenants_tenant_id");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tenant_memberships_users_user_id");
    }
}
