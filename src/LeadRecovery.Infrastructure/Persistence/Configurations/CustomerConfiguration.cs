using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customers");
        builder.HasKey(customer => customer.Id).HasName("pk_customers");
        builder.HasAlternateKey(customer => new { customer.TenantId, customer.Id })
            .HasName("ak_customers_tenant_id_id");

        builder.Property(customer => customer.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(customer => customer.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(customer => customer.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_customers_tenants_tenant_id");

        builder.Property(customer => customer.PhoneE164)
            .HasColumnName("phone_e164")
            .HasMaxLength(CustomerFieldLimits.PhoneE164MaximumLength)
            .IsRequired();

        builder.HasIndex(customer => new { customer.TenantId, customer.PhoneE164 })
            .IsUnique()
            .HasDatabaseName("ux_customers_tenant_id_phone_e164");

        builder.Property(customer => customer.Name)
            .HasColumnName("name")
            .HasMaxLength(CustomerFieldLimits.NameMaximumLength);

        builder.Property(customer => customer.Email)
            .HasColumnName("email")
            .HasMaxLength(CustomerFieldLimits.EmailMaximumLength);

        builder.Property(customer => customer.City)
            .HasColumnName("city")
            .HasMaxLength(CustomerFieldLimits.CityMaximumLength);

        builder.Property(customer => customer.PostalCode)
            .HasColumnName("postal_code")
            .HasMaxLength(CustomerFieldLimits.PostalCodeMaximumLength);

        builder.Property(customer => customer.SmsConsentBasis)
            .HasColumnName("sms_consent_basis")
            .HasMaxLength(CustomerFieldLimits.SmsConsentBasisMaximumLength);

        builder.Property(customer => customer.OptedOutAtUtc)
            .HasColumnName("opted_out_at_utc");

        builder.Property(customer => customer.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
    }
}
