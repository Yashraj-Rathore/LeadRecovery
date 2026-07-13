using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "tenants",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_tenants_status",
                "status in ('Trial', 'Active', 'Suspended', 'Closed')"));

        builder.HasKey(tenant => tenant.Id).HasName("pk_tenants");

        builder.Property(tenant => tenant.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(tenant => tenant.Name)
            .HasColumnName("name")
            .HasMaxLength(TenantFieldLimits.NameMaximumLength)
            .IsRequired();

        builder.Property(tenant => tenant.Slug)
            .HasColumnName("slug")
            .HasMaxLength(TenantFieldLimits.SlugMaximumLength)
            .IsRequired();

        builder.HasIndex(tenant => tenant.Slug)
            .IsUnique()
            .HasDatabaseName("ux_tenants_slug");

        builder.Property(tenant => tenant.TimezoneId)
            .HasColumnName("timezone_id")
            .HasMaxLength(TenantFieldLimits.TimezoneIdMaximumLength)
            .IsRequired();

        builder.Property(tenant => tenant.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(tenant => tenant.AutomationEnabled)
            .HasColumnName("automation_enabled")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(tenant => tenant.Version)
            .HasColumnName("version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(tenant => tenant.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(tenant => tenant.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();
    }
}
