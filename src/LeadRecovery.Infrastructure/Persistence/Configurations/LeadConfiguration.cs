using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "leads",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_leads_source",
                    "source in ('MissedCall', 'InboundSms', 'WebForm', 'Manual', 'Import')");
                tableBuilder.HasCheckConstraint(
                    "ck_leads_status",
                    "status in ('New', 'Contacting', 'AwaitingCustomer', 'Qualified', " +
                    "'BookingOffered', 'NeedsHuman', 'Booked', 'Closed', 'ClosedWon')");
                tableBuilder.HasCheckConstraint(
                    "ck_leads_urgency",
                    "urgency in ('Unknown', 'Low', 'Normal', 'High', 'CriticalReview')");
                tableBuilder.HasCheckConstraint(
                    "ck_leads_automation_state",
                    "automation_state in ('Active', 'PausedByUser', 'PausedBySystem', " +
                    "'Completed', 'SuppressedOptOut', 'SuppressedPolicy')");
            });

        builder.HasKey(lead => lead.Id).HasName("pk_leads");
        builder.HasAlternateKey(lead => new { lead.TenantId, lead.Id })
            .HasName("ak_leads_tenant_id_id");

        builder.Property(lead => lead.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(lead => lead.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(lead => lead.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_leads_tenants_tenant_id");

        builder.Property(lead => lead.CustomerId)
            .HasColumnName("customer_id");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(lead => new { lead.TenantId, lead.CustomerId })
            .HasPrincipalKey(customer => new { customer.TenantId, customer.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_leads_customers_tenant_id_customer_id");

        builder.Property(lead => lead.PrimaryPhoneE164)
            .HasColumnName("primary_phone_e164")
            .HasMaxLength(LeadFieldLimits.PrimaryPhoneMaximumLength)
            .IsRequired();

        builder.Property(lead => lead.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(LeadFieldLimits.DisplayNameMaximumLength);

        builder.Property(lead => lead.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(lead => lead.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(lead => lead.Urgency)
            .HasColumnName("urgency")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(lead => lead.ServiceCategoryId)
            .HasColumnName("service_category_id");

        builder.Property(lead => lead.AssignedUserId)
            .HasColumnName("assigned_user_id");

        builder.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(lead => new { lead.TenantId, lead.AssignedUserId })
            .HasPrincipalKey(membership => new
            {
                membership.TenantId,
                membership.UserId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_leads_memberships_tenant_id_assigned_user_id");

        builder.Property(lead => lead.AutomationState)
            .HasColumnName("automation_state")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(lead => lead.LastCustomerActivityAtUtc)
            .HasColumnName("last_customer_activity_at_utc");

        builder.Property(lead => lead.LastBusinessActivityAtUtc)
            .HasColumnName("last_business_activity_at_utc");

        builder.Property(lead => lead.BookedAtUtc)
            .HasColumnName("booked_at_utc");

        builder.Property(lead => lead.ClosedAtUtc)
            .HasColumnName("closed_at_utc");

        builder.Property(lead => lead.CloseReason)
            .HasColumnName("close_reason")
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(lead => lead.Version)
            .HasColumnName("version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(lead => lead.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(lead => lead.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex(lead => new { lead.TenantId, lead.Status, lead.CreatedAtUtc })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_leads_tenant_status_created");
        builder.HasIndex(
                lead => new { lead.TenantId, lead.PrimaryPhoneE164, lead.CreatedAtUtc })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_leads_tenant_phone_created");
        builder.HasIndex(lead => new { lead.TenantId, lead.AssignedUserId, lead.Status })
            .HasDatabaseName("ix_leads_tenant_assigned_status");
        builder.HasIndex(lead => new { lead.TenantId, lead.Urgency, lead.Status })
            .HasDatabaseName("ix_leads_tenant_urgency_status");
    }
}
