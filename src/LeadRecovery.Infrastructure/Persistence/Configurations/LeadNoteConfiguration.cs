using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class LeadNoteConfiguration : IEntityTypeConfiguration<LeadNote>
{
    public void Configure(EntityTypeBuilder<LeadNote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("lead_notes");
        builder.HasKey(note => note.Id).HasName("pk_lead_notes");
        builder.HasAlternateKey(note => new { note.TenantId, note.Id })
            .HasName("ak_lead_notes_tenant_id_id");

        builder.Property(note => note.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(note => note.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(note => note.LeadId)
            .HasColumnName("lead_id")
            .IsRequired();
        builder.Property(note => note.AuthorUserId)
            .HasColumnName("author_user_id")
            .IsRequired();
        builder.Property(note => note.Body)
            .HasColumnName("body")
            .HasMaxLength(LeadNoteFieldLimits.BodyMaximumLength)
            .IsRequired();
        builder.Property(note => note.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(note => note.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_lead_notes_tenants_tenant_id");
        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(note => new { note.TenantId, note.LeadId })
            .HasPrincipalKey(lead => new { lead.TenantId, lead.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_lead_notes_leads_tenant_id_lead_id");
        builder.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(note => new { note.TenantId, note.AuthorUserId })
            .HasPrincipalKey(membership => new
            {
                membership.TenantId,
                membership.UserId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_lead_notes_memberships_tenant_id_author_user_id");

        builder.HasIndex(note => new { note.TenantId, note.LeadId, note.CreatedAtUtc })
            .HasDatabaseName("ix_lead_notes_tenant_lead_created");
    }
}
