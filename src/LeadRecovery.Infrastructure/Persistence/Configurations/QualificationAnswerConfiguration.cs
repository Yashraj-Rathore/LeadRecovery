using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class QualificationAnswerConfiguration
    : IEntityTypeConfiguration<QualificationAnswer>
{
    public void Configure(EntityTypeBuilder<QualificationAnswer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(
            "qualification_answers",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_qualification_answers_outcome",
                "outcome in ('Accepted', 'Unknown', 'Ambiguous')"));
        builder.HasKey(answer => answer.Id).HasName("pk_qualification_answers");
        builder.HasAlternateKey(answer => new { answer.TenantId, answer.Id })
            .HasName("ak_qualification_answers_tenant_id_id");
        builder.Property(answer => answer.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(answer => answer.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(answer => answer.LeadId)
            .HasColumnName("lead_id")
            .IsRequired();
        builder.Property(answer => answer.SourceMessageId)
            .HasColumnName("source_message_id")
            .IsRequired();
        builder.Property(answer => answer.QuestionKey)
            .HasColumnName("question_key")
            .HasMaxLength(WorkflowDefinitionFieldLimits.QuestionKeyMaximumLength)
            .IsRequired();
        builder.Property(answer => answer.Value)
            .HasColumnName("value")
            .HasMaxLength(WorkflowDefinitionFieldLimits.AnswerValueMaximumLength);
        builder.Property(answer => answer.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(answer => answer.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
        builder.HasIndex(answer => new { answer.TenantId, answer.LeadId, answer.QuestionKey })
            .IsUnique()
            .HasDatabaseName("ux_qualification_answers_tenant_lead_question");
        builder.HasIndex(answer => new { answer.TenantId, answer.SourceMessageId })
            .IsUnique()
            .HasDatabaseName("ux_qualification_answers_tenant_source_message");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(answer => answer.TenantId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_qualification_answers_tenants_tenant_id");
        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(answer => new { answer.TenantId, answer.LeadId })
            .HasPrincipalKey(lead => new { lead.TenantId, lead.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_qualification_answers_leads_tenant_id_lead_id");
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(answer => new { answer.TenantId, answer.SourceMessageId })
            .HasPrincipalKey(message => new { message.TenantId, message.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_qualification_answers_messages_tenant_id_source_message_id");
    }
}
