using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class TenantPhoneNumberConfiguration
    : IEntityTypeConfiguration<TenantPhoneNumber>
{
    public void Configure(EntityTypeBuilder<TenantPhoneNumber> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "tenant_phone_numbers",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_tenant_phone_numbers_initial_delay_seconds",
                    "initial_delay_seconds between 0 and 3600");
                tableBuilder.HasCheckConstraint(
                    "ck_tenant_phone_numbers_recovery_cooldown_seconds",
                    "recovery_cooldown_seconds between 1 and 86400");
                tableBuilder.HasCheckConstraint(
                    "ck_tenant_phone_numbers_recoverable_statuses",
                    "cardinality(recoverable_call_statuses) > 0");
            });

        builder.HasKey(number => number.Id).HasName("pk_tenant_phone_numbers");
        builder.HasAlternateKey(number => new { number.TenantId, number.Id })
            .HasName("ak_tenant_phone_numbers_tenant_id_id");

        builder.Property(number => number.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(number => number.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(number => number.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tenant_phone_numbers_tenants_tenant_id");
        builder.Property(number => number.Provider)
            .HasColumnName("provider")
            .HasMaxLength(TenantPhoneNumberFieldLimits.ProviderMaximumLength)
            .IsRequired();
        builder.Property(number => number.PhoneNumberE164)
            .HasColumnName("phone_number_e164")
            .HasMaxLength(TenantPhoneNumberFieldLimits.PhoneNumberMaximumLength)
            .IsRequired();
        builder.Property(number => number.ProviderNumberSid)
            .HasColumnName("provider_number_sid")
            .HasMaxLength(TenantPhoneNumberFieldLimits.ProviderNumberSidMaximumLength)
            .IsRequired();
        builder.Property(number => number.InboundSmsEnabled)
            .HasColumnName("inbound_sms_enabled")
            .HasDefaultValue(true)
            .IsRequired();
        builder.Property(number => number.MissedCallRecoveryEnabled)
            .HasColumnName("missed_call_recovery_enabled")
            .HasDefaultValue(true)
            .IsRequired();
        builder.Property(number => number.IsPrimary)
            .HasColumnName("is_primary")
            .HasDefaultValue(false)
            .IsRequired();

        ValueComparer<string[]> statusesComparer = new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            value => value.ToArray());
        builder.Property(number => number.RecoverableCallStatuses)
            .HasColumnName("recoverable_call_statuses")
            .HasColumnType("text[]")
            .Metadata.SetValueComparer(statusesComparer);
        builder.Property(number => number.InitialDelaySeconds)
            .HasColumnName("initial_delay_seconds")
            .IsRequired();
        builder.Property(number => number.RecoveryCooldownSeconds)
            .HasColumnName("recovery_cooldown_seconds")
            .IsRequired();

        builder.HasIndex(number => new { number.Provider, number.ProviderNumberSid })
            .IsUnique()
            .HasDatabaseName("ux_tenant_phone_numbers_provider_number_sid");
        builder.HasIndex(number => new { number.Provider, number.PhoneNumberE164 })
            .IsUnique()
            .HasDatabaseName("ux_tenant_phone_numbers_provider_phone_number");
        builder.HasIndex(number => new { number.TenantId, number.PhoneNumberE164 })
            .IsUnique()
            .HasDatabaseName("ux_tenant_phone_numbers_tenant_phone_number");
    }
}
