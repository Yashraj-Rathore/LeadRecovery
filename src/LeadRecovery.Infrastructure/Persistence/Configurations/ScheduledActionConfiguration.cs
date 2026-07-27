using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class ScheduledActionConfiguration : IEntityTypeConfiguration<ScheduledAction>
{
    public void Configure(EntityTypeBuilder<ScheduledAction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "scheduled_actions",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_scheduled_actions_status",
                    "status in ('Pending', 'Running', 'Completed', 'Cancelled', 'Failed')");
                tableBuilder.HasCheckConstraint(
                    "ck_scheduled_actions_attempt_count",
                    "attempt_count >= 0");
            });

        builder.HasKey(action => action.Id).HasName("pk_scheduled_actions");
        builder.HasAlternateKey(action => new { action.TenantId, action.Id })
            .HasName("ak_scheduled_actions_tenant_id_id");

        builder.Property(action => action.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(action => action.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(action => action.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_scheduled_actions_tenants_tenant_id");

        builder.Property(action => action.LeadId)
            .HasColumnName("lead_id")
            .IsRequired();

        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(action => new { action.TenantId, action.LeadId })
            .HasPrincipalKey(lead => new { lead.TenantId, lead.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_scheduled_actions_leads_tenant_id_lead_id");

        builder.Property(action => action.ActionType)
            .HasColumnName("action_type")
            .HasMaxLength(ScheduledActionFieldLimits.ActionTypeMaximumLength)
            .IsRequired();

        builder.Property(action => action.ScheduledForUtc)
            .HasColumnName("scheduled_for_utc")
            .IsRequired();

        builder.Property(action => action.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(action => action.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(action => action.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(ScheduledActionFieldLimits.IdempotencyKeyMaximumLength)
            .IsRequired();

        builder.HasIndex(action => new { action.TenantId, action.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_scheduled_actions_tenant_id_idempotency_key");

        builder.Property(action => action.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(action => action.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(ScheduledActionFieldLimits.LastErrorMaximumLength);

        builder.Property(action => action.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(ScheduledActionFieldLimits.CorrelationIdMaximumLength);

        builder.Property(action => action.TraceParent)
            .HasColumnName("trace_parent")
            .HasMaxLength(ScheduledActionFieldLimits.TraceParentMaximumLength);

        builder.Property(action => action.TraceState)
            .HasColumnName("trace_state")
            .HasMaxLength(ScheduledActionFieldLimits.TraceStateMaximumLength);

        builder.Property(action => action.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(action => action.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex(action => new { action.Status, action.ScheduledForUtc })
            .HasDatabaseName("ix_scheduled_actions_due");
        builder.HasIndex(action => new { action.TenantId, action.LeadId, action.Status })
            .HasDatabaseName("ix_scheduled_actions_tenant_lead_status");
    }
}
