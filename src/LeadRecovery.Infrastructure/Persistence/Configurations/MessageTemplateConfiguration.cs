using LeadRecovery.Domain.Conversations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");
        builder.HasKey(template => template.Id).HasName("pk_message_templates");
        builder.Property(template => template.Id).HasColumnName("id");
        builder.Property(template => template.TenantId).HasColumnName("tenant_id");
        builder.Property(template => template.Name)
            .HasColumnName("name")
            .HasMaxLength(MessageTemplateFieldLimits.NameMaximumLength)
            .IsRequired();
        builder.Property(template => template.Purpose)
            .HasColumnName("purpose")
            .HasMaxLength(MessageTemplateFieldLimits.PurposeMaximumLength)
            .IsRequired();
        builder.Property(template => template.Body)
            .HasColumnName("body")
            .HasMaxLength(MessageTemplateFieldLimits.BodyMaximumLength)
            .IsRequired();
        builder.Property(template => template.Version).HasColumnName("version");
        builder.Property(template => template.IsApproved).HasColumnName("is_approved");
        builder.Property(template => template.IsActive).HasColumnName("is_active");
        builder.Property(template => template.CreatedByUserId)
            .HasColumnName("created_by_user_id");
        builder.Property(template => template.ApprovedByUserId)
            .HasColumnName("approved_by_user_id");
        builder.Property(template => template.CreatedAtUtc)
            .HasColumnName("created_at_utc");
        builder.Property(template => template.ApprovedAtUtc)
            .HasColumnName("approved_at_utc");

        builder.HasIndex(template => new { template.TenantId, template.Purpose })
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("ux_message_templates_tenant_purpose_active");
        builder.HasAlternateKey(template => new { template.TenantId, template.Id })
            .HasName("ak_message_templates_tenant_id_id");
        builder.HasOne<LeadRecovery.Domain.Tenancy.Tenant>()
            .WithMany()
            .HasForeignKey(template => template.TenantId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_message_templates_tenants_tenant_id");
    }
}
