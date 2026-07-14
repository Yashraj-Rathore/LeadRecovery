using LeadRecovery.Infrastructure.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadRecovery.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration
    : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");
        builder.HasKey(user => user.Id).HasName("pk_users");

        builder.Property(user => user.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(user => user.UserName)
            .HasColumnName("user_name")
            .HasMaxLength(256);
        builder.Property(user => user.NormalizedUserName)
            .HasColumnName("normalized_user_name")
            .HasMaxLength(256);
        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(320);
        builder.Property(user => user.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(320);
        builder.Property(user => user.EmailConfirmed)
            .HasColumnName("email_confirmed");
        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash");
        builder.Property(user => user.SecurityStamp)
            .HasColumnName("security_stamp");
        builder.Property(user => user.ConcurrencyStamp)
            .HasColumnName("concurrency_stamp")
            .IsConcurrencyToken();
        builder.Property(user => user.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(32);
        builder.Property(user => user.PhoneNumberConfirmed)
            .HasColumnName("phone_number_confirmed");
        builder.Property(user => user.TwoFactorEnabled)
            .HasColumnName("two_factor_enabled");
        builder.Property(user => user.LockoutEnd)
            .HasColumnName("lockout_end");
        builder.Property(user => user.LockoutEnabled)
            .HasColumnName("lockout_enabled");
        builder.Property(user => user.AccessFailedCount)
            .HasColumnName("access_failed_count");
        builder.Property(user => user.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(ApplicationUserFieldLimits.DisplayNameMaximumLength)
            .IsRequired();
        builder.Property(user => user.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(user => user.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(user => user.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_user_name");
        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_email");
    }
}
