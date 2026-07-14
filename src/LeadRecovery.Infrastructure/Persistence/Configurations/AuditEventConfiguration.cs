using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_events");
        builder.HasKey(auditEvent => auditEvent.Id).HasName("pk_audit_events");
        builder.Property(auditEvent => auditEvent.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(auditEvent => auditEvent.TenantId)
            .HasColumnName("tenant_id");
        builder.Property(auditEvent => auditEvent.ActorType)
            .HasColumnName("actor_type")
            .HasMaxLength(AuditEventFieldLimits.ActorTypeMaximumLength)
            .IsRequired();
        builder.Property(auditEvent => auditEvent.ActorId)
            .HasColumnName("actor_id")
            .HasMaxLength(AuditEventFieldLimits.ActorIdMaximumLength);
        builder.Property(auditEvent => auditEvent.Action)
            .HasColumnName("action")
            .HasMaxLength(AuditEventFieldLimits.ActionMaximumLength)
            .IsRequired();
        builder.Property(auditEvent => auditEvent.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(AuditEventFieldLimits.EntityTypeMaximumLength)
            .IsRequired();
        builder.Property(auditEvent => auditEvent.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(AuditEventFieldLimits.EntityIdMaximumLength)
            .IsRequired();
        builder.Property(auditEvent => auditEvent.BeforeJson)
            .HasColumnName("before_json")
            .HasColumnType("jsonb");
        builder.Property(auditEvent => auditEvent.AfterJson)
            .HasColumnName("after_json")
            .HasColumnType("jsonb");
        builder.Property(auditEvent => auditEvent.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(AuditEventFieldLimits.CorrelationIdMaximumLength)
            .IsRequired();
        builder.Property(auditEvent => auditEvent.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_audit_events_tenants_tenant_id");
        builder.HasIndex(auditEvent => new
        {
            auditEvent.TenantId,
            auditEvent.CreatedAtUtc,
        })
            .IsDescending(false, true)
            .HasDatabaseName("ix_audit_events_tenant_created");
    }
}
