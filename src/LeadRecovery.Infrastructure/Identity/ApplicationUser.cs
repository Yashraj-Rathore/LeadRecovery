using Microsoft.AspNetCore.Identity;

namespace LeadRecovery.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    private ApplicationUser()
    {
    }

    public ApplicationUser(
        Guid id,
        string email,
        string displayName,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty user ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        string normalizedDisplayName = NormalizeDisplayName(displayName);
        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", nameof(createdAtUtc));
        }

        Id = id;
        Email = email.Trim();
        UserName = Email;
        DisplayName = normalizedDisplayName;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string normalized = displayName.Trim();
        if (normalized.Length > ApplicationUserFieldLimits.DisplayNameMaximumLength)
        {
            throw new ArgumentException(
                $"The display name cannot exceed " +
                $"{ApplicationUserFieldLimits.DisplayNameMaximumLength} characters.",
                nameof(displayName));
        }

        return normalized;
    }
}
