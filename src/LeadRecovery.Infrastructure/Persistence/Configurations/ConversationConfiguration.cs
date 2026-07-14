using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "conversations",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_conversations_channel",
                    "channel in ('Sms')");
                tableBuilder.HasCheckConstraint(
                    "ck_conversations_status",
                    "status in ('Open', 'Closed')");
            });

        builder.HasKey(conversation => conversation.Id)
            .HasName("pk_conversations");
        builder.HasAlternateKey(conversation => new { conversation.TenantId, conversation.Id })
            .HasName("ak_conversations_tenant_id_id");

        builder.Property(conversation => conversation.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(conversation => conversation.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(conversation => conversation.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_conversations_tenants_tenant_id");

        builder.Property(conversation => conversation.LeadId)
            .HasColumnName("lead_id")
            .IsRequired();

        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(conversation => new { conversation.TenantId, conversation.LeadId })
            .HasPrincipalKey(lead => new { lead.TenantId, lead.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_conversations_leads_tenant_id_lead_id");

        builder.Property(conversation => conversation.Channel)
            .HasColumnName("channel")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(conversation => conversation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(conversation => conversation.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(conversation => conversation.ClosedAtUtc)
            .HasColumnName("closed_at_utc");

        builder.HasIndex(
                conversation => new
                {
                    conversation.TenantId,
                    conversation.LeadId,
                    conversation.CreatedAtUtc,
                })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_conversations_tenant_lead_created");
    }
}
