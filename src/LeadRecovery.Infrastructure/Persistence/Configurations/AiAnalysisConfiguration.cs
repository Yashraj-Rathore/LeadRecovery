using LeadRecovery.Domain.Analysis;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class AiAnalysisConfiguration : IEntityTypeConfiguration<AiAnalysis>
{
    public void Configure(EntityTypeBuilder<AiAnalysis> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "ai_analyses",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_ai_analyses_confidence",
                    "confidence >= 0 and confidence <= 1");
                tableBuilder.HasCheckConstraint(
                    "ck_ai_analyses_review_status",
                    "review_status in ('Pending', 'Accepted', 'Edited', 'Rejected')");
                tableBuilder.HasCheckConstraint(
                    "ck_ai_analyses_urgency_suggestion",
                    "urgency_suggestion in ('Unknown', 'Low', 'Normal', 'High', 'CriticalReview')");
                tableBuilder.HasCheckConstraint(
                    "ck_ai_analyses_reviewed_urgency",
                    "reviewed_urgency is null or reviewed_urgency in ('Unknown', 'Low', 'Normal', 'High', 'CriticalReview')");
                tableBuilder.HasCheckConstraint(
                    "ck_ai_analyses_review_metadata",
                    """
                    (review_status = 'Pending' and reviewed_by_user_id is null and reviewed_at_utc is null)
                    or
                    (review_status <> 'Pending' and reviewed_by_user_id is not null and reviewed_at_utc is not null)
                    """);
                tableBuilder.HasCheckConstraint(
                    "ck_ai_analyses_review_values",
                    """
                    (review_status in ('Accepted', 'Edited') and reviewed_category is not null and reviewed_urgency is not null and reviewed_summary is not null)
                    or
                    (review_status in ('Pending', 'Rejected') and reviewed_category is null and reviewed_urgency is null and reviewed_summary is null)
                    """);
            });
        builder.HasKey(analysis => analysis.Id).HasName("pk_ai_analyses");
        builder.HasAlternateKey(analysis => new { analysis.TenantId, analysis.Id })
            .HasName("ak_ai_analyses_tenant_id_id");

        builder.Property(analysis => analysis.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(analysis => analysis.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(analysis => analysis.LeadId)
            .HasColumnName("lead_id")
            .IsRequired();
        builder.Property(analysis => analysis.SchemaVersion)
            .HasColumnName("schema_version")
            .HasMaxLength(AiAnalysisFieldLimits.SchemaVersionMaximumLength)
            .IsRequired();
        builder.Property(analysis => analysis.Provider)
            .HasColumnName("provider")
            .HasMaxLength(AiAnalysisFieldLimits.ProviderMaximumLength)
            .IsRequired();
        builder.Property(analysis => analysis.ModelReference)
            .HasColumnName("model_reference")
            .HasMaxLength(AiAnalysisFieldLimits.ModelReferenceMaximumLength)
            .IsRequired();
        builder.Property(analysis => analysis.InputHash)
            .HasColumnName("input_hash")
            .HasMaxLength(AiAnalysisFieldLimits.InputHashLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(analysis => analysis.AllowedCategoriesJson)
            .HasColumnName("allowed_categories_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(analysis => analysis.CategorySuggestion)
            .HasColumnName("category_suggestion")
            .HasMaxLength(AiAnalysisFieldLimits.CategoryMaximumLength)
            .IsRequired();
        builder.Property(analysis => analysis.UrgencySuggestion)
            .HasColumnName("urgency_suggestion")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(analysis => analysis.Summary)
            .HasColumnName("summary")
            .HasMaxLength(AiAnalysisFieldLimits.SummaryMaximumLength)
            .IsRequired();
        builder.Property(analysis => analysis.ExtractedCity)
            .HasColumnName("extracted_city")
            .HasMaxLength(AiAnalysisFieldLimits.ExtractedValueMaximumLength);
        builder.Property(analysis => analysis.ExtractedPostalCode)
            .HasColumnName("extracted_postal_code")
            .HasMaxLength(AiAnalysisFieldLimits.ExtractedValueMaximumLength);
        builder.Property(analysis => analysis.ExtractedPreferredCallbackWindow)
            .HasColumnName("extracted_preferred_callback_window")
            .HasMaxLength(AiAnalysisFieldLimits.ExtractedValueMaximumLength);
        builder.Property(analysis => analysis.SuggestedReply)
            .HasColumnName("suggested_reply")
            .HasMaxLength(AiAnalysisFieldLimits.SuggestedReplyMaximumLength);
        builder.Property(analysis => analysis.Confidence)
            .HasColumnName("confidence")
            .IsRequired();
        builder.Property(analysis => analysis.RequiresHumanReview)
            .HasColumnName("requires_human_review")
            .IsRequired();
        builder.Property(analysis => analysis.ReasonCodesJson)
            .HasColumnName("reason_codes_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(analysis => analysis.RawStructuredOutputJson)
            .HasColumnName("raw_structured_output_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(analysis => analysis.ReviewStatus)
            .HasColumnName("review_status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(analysis => analysis.ReviewedCategory)
            .HasColumnName("reviewed_category")
            .HasMaxLength(AiAnalysisFieldLimits.CategoryMaximumLength);
        builder.Property(analysis => analysis.ReviewedUrgency)
            .HasColumnName("reviewed_urgency")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(analysis => analysis.ReviewedSummary)
            .HasColumnName("reviewed_summary")
            .HasMaxLength(AiAnalysisFieldLimits.SummaryMaximumLength);
        builder.Property(analysis => analysis.ReviewedCity)
            .HasColumnName("reviewed_city")
            .HasMaxLength(AiAnalysisFieldLimits.ExtractedValueMaximumLength);
        builder.Property(analysis => analysis.ReviewedPostalCode)
            .HasColumnName("reviewed_postal_code")
            .HasMaxLength(AiAnalysisFieldLimits.ExtractedValueMaximumLength);
        builder.Property(analysis => analysis.ReviewedPreferredCallbackWindow)
            .HasColumnName("reviewed_preferred_callback_window")
            .HasMaxLength(AiAnalysisFieldLimits.ExtractedValueMaximumLength);
        builder.Property(analysis => analysis.ReviewedSuggestedReply)
            .HasColumnName("reviewed_suggested_reply")
            .HasMaxLength(AiAnalysisFieldLimits.SuggestedReplyMaximumLength);
        builder.Property(analysis => analysis.CorrectionReason)
            .HasColumnName("correction_reason")
            .HasMaxLength(AiAnalysisFieldLimits.CorrectionReasonMaximumLength);
        builder.Property(analysis => analysis.ReviewedByUserId)
            .HasColumnName("reviewed_by_user_id");
        builder.Property(analysis => analysis.ReviewedAtUtc)
            .HasColumnName("reviewed_at_utc");
        builder.Property(analysis => analysis.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(analysis => analysis.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(analysis => analysis.TenantId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_ai_analyses_tenants_tenant_id");
        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(analysis => new { analysis.TenantId, analysis.LeadId })
            .HasPrincipalKey(lead => new { lead.TenantId, lead.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_ai_analyses_leads_tenant_id_lead_id");
        builder.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(analysis => new
            {
                analysis.TenantId,
                UserId = analysis.ReviewedByUserId,
            })
            .HasPrincipalKey(membership => new
            {
                membership.TenantId,
                membership.UserId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_ai_analyses_memberships_tenant_id_reviewed_by_user_id");

        builder.HasIndex(analysis => new
        {
            analysis.TenantId,
            analysis.LeadId,
            analysis.SchemaVersion,
            analysis.InputHash,
        })
            .IsUnique()
            .HasDatabaseName("ux_ai_analyses_tenant_lead_schema_input_hash");
        builder.HasIndex(analysis => new
        {
            analysis.TenantId,
            analysis.LeadId,
            analysis.ReviewStatus,
            analysis.CreatedAtUtc,
        })
            .HasDatabaseName("ix_ai_analyses_tenant_lead_review_created");
        builder.HasIndex(analysis => new
        {
            analysis.TenantId,
            analysis.ReviewedByUserId,
        })
            .HasDatabaseName("ix_ai_analyses_tenant_reviewer");
    }
}
