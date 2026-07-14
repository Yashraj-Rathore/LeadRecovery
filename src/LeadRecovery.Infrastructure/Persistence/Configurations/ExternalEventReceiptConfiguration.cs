using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class ExternalEventReceiptConfiguration
    : IEntityTypeConfiguration<ExternalEventReceipt>
{
    public void Configure(EntityTypeBuilder<ExternalEventReceipt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("external_event_receipts");
        builder.HasKey(receipt => receipt.Id).HasName("pk_external_event_receipts");

        builder.Property(receipt => receipt.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(receipt => receipt.TenantId)
            .HasColumnName("tenant_id");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(receipt => receipt.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_external_event_receipts_tenants_tenant_id");

        builder.Property(receipt => receipt.Provider)
            .HasColumnName("provider")
            .HasMaxLength(ExternalEventReceiptFieldLimits.ProviderMaximumLength)
            .IsRequired();

        builder.Property(receipt => receipt.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(ExternalEventReceiptFieldLimits.EventTypeMaximumLength)
            .IsRequired();

        builder.Property(receipt => receipt.ExternalEventId)
            .HasColumnName("external_event_id")
            .HasMaxLength(ExternalEventReceiptFieldLimits.ExternalEventIdMaximumLength)
            .IsRequired();

        builder.HasIndex(
                receipt => new
                {
                    receipt.Provider,
                    receipt.EventType,
                    receipt.ExternalEventId,
                })
            .IsUnique()
            .HasDatabaseName("ux_external_event_receipts_provider_event_identity");

        builder.Property(receipt => receipt.PayloadHash)
            .HasColumnName("payload_hash")
            .HasMaxLength(ExternalEventReceiptFieldLimits.PayloadHashMaximumLength)
            .IsRequired();

        builder.Property(receipt => receipt.ReceivedAtUtc)
            .HasColumnName("received_at_utc")
            .IsRequired();

        builder.Property(receipt => receipt.ProcessedAtUtc)
            .HasColumnName("processed_at_utc");

        builder.Property(receipt => receipt.ProcessingResult)
            .HasColumnName("processing_result")
            .HasMaxLength(ExternalEventReceiptFieldLimits.ProcessingResultMaximumLength);

        builder.HasIndex(receipt => receipt.ReceivedAtUtc)
            .HasDatabaseName("ix_external_event_receipts_received_at");
    }
}
