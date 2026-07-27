using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowDefinitionConfiguration
    : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("workflow_definitions");
        builder.HasKey(workflow => workflow.Id).HasName("pk_workflow_definitions");
        builder.HasAlternateKey(workflow => new { workflow.TenantId, workflow.Id })
            .HasName("ak_workflow_definitions_tenant_id_id");
        builder.Property(workflow => workflow.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(workflow => workflow.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(workflow => workflow.Name)
            .HasColumnName("name")
            .HasMaxLength(WorkflowDefinitionFieldLimits.NameMaximumLength)
            .IsRequired();
        builder.Property(workflow => workflow.Version)
            .HasColumnName("version")
            .IsRequired();
        builder.Property(workflow => workflow.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(workflow => workflow.BookingUrl)
            .HasColumnName("booking_url")
            .HasMaxLength(WorkflowDefinitionFieldLimits.BookingUrlMaximumLength)
            .IsRequired();
        builder.Property(workflow => workflow.FollowUpPolicyJson)
            .HasColumnName("follow_up_policy_json")
            .HasMaxLength(WorkflowDefinitionFieldLimits.PolicyJsonMaximumLength)
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(workflow => workflow.BusinessHoursPolicyJson)
            .HasColumnName("business_hours_policy_json")
            .HasMaxLength(WorkflowDefinitionFieldLimits.PolicyJsonMaximumLength)
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(workflow => workflow.QualificationPolicyJson)
            .HasColumnName("qualification_policy_json")
            .HasMaxLength(WorkflowDefinitionFieldLimits.PolicyJsonMaximumLength)
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(workflow => workflow.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
        builder.Property(workflow => workflow.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();
        builder.HasIndex(workflow => new { workflow.TenantId, workflow.Version })
            .IsUnique()
            .HasDatabaseName("ux_workflow_definitions_tenant_version");
        builder.HasIndex(workflow => workflow.TenantId)
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("ux_workflow_definitions_tenant_active");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(workflow => workflow.TenantId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_workflow_definitions_tenants_tenant_id");
    }
}
