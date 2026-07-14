using System.Security.Claims;

using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Persistence;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Api.Demo;

internal sealed class DemoDataSeeder(
    LeadRecoveryDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    public async Task SeedAsync(
        DemoSeedSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DateTimeOffset now = timeProvider.GetUtcNow();

        Tenant alpha = await EnsureTenant(
            "Alpha Plumbing",
            "alpha-plumbing",
            now,
            cancellationToken);
        Tenant beta = await EnsureTenant(
            "Beta HVAC",
            "beta-hvac",
            now,
            cancellationToken);

        ApplicationUser owner = await EnsureUser(
            settings.OwnerEmail,
            settings.OwnerPassword,
            "Alpha Owner",
            now);
        ApplicationUser staff = await EnsureUser(
            settings.StaffEmail,
            settings.StaffPassword,
            "Alpha Staff",
            now);
        ApplicationUser betaOwner = await EnsureUser(
            settings.BetaOwnerEmail,
            settings.BetaOwnerPassword,
            "Beta Owner",
            now);

        await EnsureTenantData(
            alpha,
            [(owner, TenantRole.Owner), (staff, TenantRole.Staff)],
            [
                new DemoLead(
                    settings.AlphaUrgentPhone,
                    "Urgent plumbing caller",
                    LeadSource.MissedCall,
                    RequiresHumanReview: true),
                new DemoLead(
                    settings.AlphaBookingPhone,
                    "Booking request",
                    LeadSource.InboundSms,
                    RequiresHumanReview: false),
            ],
            now,
            cancellationToken);
        await EnsureTenantData(
            beta,
            [(betaOwner, TenantRole.Owner)],
            [
                new DemoLead(
                    settings.BetaLeadPhone,
                    "Beta tenant lead",
                    LeadSource.Manual,
                    RequiresHumanReview: false),
            ],
            now,
            cancellationToken);
    }

    private async Task<Tenant> EnsureTenant(
        string name,
        string slug,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Tenant? existing = await dbContext.Tenants.SingleOrDefaultAsync(
            tenant => tenant.Slug == slug,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        Tenant tenant = new(
            Guid.CreateVersion7(),
            name,
            slug,
            "America/Toronto",
            now);
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);
        return tenant;
    }

    private async Task<ApplicationUser> EnsureUser(
        string email,
        string password,
        string displayName,
        DateTimeOffset now)
    {
        ApplicationUser? existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        ApplicationUser user = new(Guid.CreateVersion7(), email, displayName, now)
        {
            EmailConfirmed = true,
        };
        IdentityResult result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            string codes = string.Join(", ", result.Errors.Select(error => error.Code));
            throw new InvalidOperationException(
                $"Demo user creation failed identity validation: {codes}.");
        }

        return user;
    }

    private async Task EnsureTenantData(
        Tenant tenant,
        IReadOnlyCollection<(ApplicationUser User, TenantRole Role)> memberships,
        IReadOnlyCollection<DemoLead> leads,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        HttpContext? previousContext = httpContextAccessor.HttpContext;
        try
        {
            ClaimsIdentity identity = new(
                [new Claim(TenantClaimTypes.TenantId, tenant.Id.ToString())],
                "DemoSeed");
            httpContextAccessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            };

            foreach ((ApplicationUser user, TenantRole role) in memberships)
            {
                bool exists = await dbContext.TenantMemberships
                    .IgnoreQueryFilters()
                    .AnyAsync(
                        membership =>
                            membership.TenantId == tenant.Id &&
                            membership.UserId == user.Id,
                        cancellationToken);
                if (!exists)
                {
                    dbContext.TenantMemberships.Add(new TenantMembership(
                        Guid.CreateVersion7(),
                        tenant.Id,
                        user.Id,
                        role,
                        now));
                }
            }

            bool tenantHasLeads = await dbContext.Leads
                .IgnoreQueryFilters()
                .AnyAsync(lead => lead.TenantId == tenant.Id, cancellationToken);
            if (!tenantHasLeads)
            {
                int offset = 0;
                foreach (DemoLead item in leads)
                {
                    DateTimeOffset createdAt = now.AddMinutes(-30 - offset);
                    Lead lead = new(
                        Guid.CreateVersion7(),
                        tenant.Id,
                        item.Phone,
                        item.Source,
                        createdAt,
                        item.DisplayName);
                    if (item.RequiresHumanReview)
                    {
                        lead.RequireHumanReview(createdAt.AddMinutes(1));
                    }

                    dbContext.Leads.Add(lead);
                    offset += 15;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            httpContextAccessor.HttpContext = previousContext;
        }
    }

    private sealed record DemoLead(
        string Phone,
        string DisplayName,
        LeadSource Source,
        bool RequiresHumanReview);
}
