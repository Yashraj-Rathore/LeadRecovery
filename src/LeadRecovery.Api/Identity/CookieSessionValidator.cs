using System.Security.Claims;

using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Persistence;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Api.Identity;

internal static class CookieSessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ClaimsPrincipal? principal = context.Principal;
        if (principal is null ||
            !Guid.TryParse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier),
                out Guid userId) ||
            userId == Guid.Empty ||
            !Guid.TryParse(
                principal.FindFirstValue(TenantClaimTypes.TenantId),
                out Guid tenantId) ||
            tenantId == Guid.Empty ||
            !Enum.TryParse(
                principal.FindFirstValue(ClaimTypes.Role),
                ignoreCase: false,
                out TenantRole role) ||
            !Enum.IsDefined(role))
        {
            await RejectAsync(context);
            return;
        }

        UserManager<ApplicationUser> userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());
        string? principalSecurityStamp = principal.FindFirstValue(
            userManager.Options.ClaimsIdentity.SecurityStampClaimType);
        if (user is null || !user.IsActive ||
            string.IsNullOrEmpty(principalSecurityStamp) ||
            !string.Equals(
                principalSecurityStamp,
                await userManager.GetSecurityStampAsync(user),
                StringComparison.Ordinal))
        {
            await RejectAsync(context);
            return;
        }

        LeadRecoveryDbContext dbContext = context.HttpContext.RequestServices
            .GetRequiredService<LeadRecoveryDbContext>();
        bool membershipIsActive = await dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .Where(membership =>
                membership.UserId == userId &&
                membership.TenantId == tenantId &&
                membership.Role == role)
            .Join(
                dbContext.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (_, tenant) => tenant.Status)
            .AnyAsync(
                status => status == TenantStatus.Trial || status == TenantStatus.Active,
                context.HttpContext.RequestAborted);
        if (!membershipIsActive)
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
