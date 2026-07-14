using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "messages",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_messages_direction",
                    "direction in ('Inbound', 'Outbound')");
                tableBuilder.HasCheckConstraint(
                    "ck_messages_kind",
                    "kind in ('Automated', 'Manual', 'System')");
                tableBuilder.HasCheckConstraint(
                    "ck_messages_status",
                    "status in ('Queued', 'Sent', 'Delivered', 'Failed', " +
                    "'Received', 'Suppressed')");
            });

        builder.HasKey(message => message.Id).HasName("pk_messages");
        builder.HasAlternateKey(message => new { message.TenantId, message.Id })
            .HasName("ak_messages_tenant_id_id");

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(message => message.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(message => message.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_messages_tenants_tenant_id");

        builder.Property(message => message.LeadId)
            .HasColumnName("lead_id")
            .IsRequired();

        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(message => new { message.TenantId, message.LeadId })
            .HasPrincipalKey(lead => new { lead.TenantId, lead.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_messages_leads_tenant_id_lead_id");

        builder.Property(message => message.ConversationId)
            .HasColumnName("conversation_id")
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(message => new { message.TenantId, message.ConversationId })
            .HasPrincipalKey(conversation => new { conversation.TenantId, conversation.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_messages_conversations_tenant_id_conversation_id");

        builder.Property(message => message.Direction)
            .HasColumnName("direction")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(message => message.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(message => message.Provider)
            .HasColumnName("provider")
            .HasMaxLength(MessageFieldLimits.ProviderMaximumLength)
            .IsRequired();

        builder.Property(message => message.ProviderMessageSid)
            .HasColumnName("provider_message_sid")
            .HasMaxLength(MessageFieldLimits.ProviderMessageSidMaximumLength);

        builder.HasIndex(message => new { message.Provider, message.ProviderMessageSid })
            .IsUnique()
            .HasFilter("provider_message_sid is not null")
            .HasDatabaseName("ux_messages_provider_sid");

        builder.Property(message => message.ClientIdempotencyKey)
            .HasColumnName("client_idempotency_key")
            .HasMaxLength(MessageFieldLimits.ClientIdempotencyKeyMaximumLength)
            .IsRequired();

        builder.HasIndex(message => new { message.TenantId, message.ClientIdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_messages_tenant_id_client_idempotency_key");

        builder.Property(message => message.Body)
            .HasColumnName("body")
            .HasMaxLength(MessageFieldLimits.BodyMaximumLength)
            .IsRequired();

        builder.Property(message => message.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(message => message.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(MessageFieldLimits.FailureCodeMaximumLength);

        builder.Property(message => message.FailureDescription)
            .HasColumnName("failure_description")
            .HasMaxLength(MessageFieldLimits.FailureDescriptionMaximumLength);

        builder.Property(message => message.SentByUserId)
            .HasColumnName("sent_by_user_id");

        builder.Property(message => message.TemplateId)
            .HasColumnName("template_id");

        builder.Property(message => message.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(message => message.SentAtUtc)
            .HasColumnName("sent_at_utc");

        builder.Property(message => message.DeliveredAtUtc)
            .HasColumnName("delivered_at_utc");

        builder.HasIndex(
                message => new
                {
                    message.TenantId,
                    message.ConversationId,
                    message.CreatedAtUtc,
                })
            .HasDatabaseName("ix_messages_tenant_conversation_created");
    }
}
