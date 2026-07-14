using System.Security.Claims;

using LeadRecovery.Application.Tenancy;
using LeadRecovery.Contracts.Authentication;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Persistence;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Api.Identity;

internal sealed class AuthenticationSessionService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    LeadRecoveryDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<AuthSessionResponse?> LoginAsync(
        string? email,
        string? password,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        ApplicationUser? user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null || !user.IsActive)
        {
            return null;
        }

        SignInResult passwordResult = await signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);
        if (!passwordResult.Succeeded)
        {
            return null;
        }

        List<MembershipSession> memberships = await dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .Where(membership => membership.UserId == user.Id)
            .Join(
                dbContext.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (membership, tenant) => new
                {
                    Membership = membership,
                    Tenant = tenant,
                })
            .Where(item =>
                item.Tenant.Status == TenantStatus.Trial ||
                item.Tenant.Status == TenantStatus.Active)
            .Select(item => new MembershipSession(
                item.Tenant.Id,
                item.Tenant.Name,
                item.Tenant.Status,
                item.Membership.Role))
            .Take(2)
            .ToListAsync(cancellationToken);
        if (memberships.Count != 1)
        {
            return null;
        }

        MembershipSession membership = memberships[0];
        List<Claim> sessionClaims =
        [
            new(TenantClaimTypes.TenantId, membership.TenantId.ToString()),
            new(TenantClaimTypes.TenantName, membership.TenantName),
            new(TenantClaimTypes.DisplayName, user.DisplayName),
            new(ClaimTypes.Role, membership.Role.ToString()),
        ];
        await signInManager.SignInWithClaimsAsync(
            user,
            isPersistent: false,
            sessionClaims);

        dbContext.AuditEvents.Add(CreateSessionAudit(
            membership.TenantId,
            user.Id,
            "Authentication.Login",
            correlationId));
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateResponse(user, membership);
    }

    public async Task<AuthSessionResponse?> GetCurrentAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!TryReadSession(principal, out Guid userId, out Guid tenantId))
        {
            return null;
        }

        CurrentSession? session = await dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .Where(membership =>
                membership.UserId == userId && membership.TenantId == tenantId)
            .Join(
                dbContext.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (membership, tenant) => new
                {
                    Membership = membership,
                    Tenant = tenant,
                })
            .Join(
                dbContext.Users,
                item => item.Membership.UserId,
                user => user.Id,
                (item, user) => new CurrentSession(
                    user,
                    item.Tenant.Id,
                    item.Tenant.Name,
                    item.Membership.Role))
            .SingleOrDefaultAsync(cancellationToken);

        return session is null
            ? null
            : new AuthSessionResponse(
                session.User.Id,
                session.User.DisplayName,
                session.User.Email ?? string.Empty,
                session.TenantId,
                session.TenantName,
                session.Role.ToString());
    }

    public async Task LogoutAsync(
        ClaimsPrincipal principal,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryReadSession(principal, out Guid userId, out Guid tenantId))
        {
            ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());
            if (user is not null)
            {
                dbContext.AuditEvents.Add(CreateSessionAudit(
                    tenantId,
                    userId,
                    "Authentication.Logout",
                    correlationId));
                IdentityResult stampResult = await userManager.UpdateSecurityStampAsync(user);
                if (!stampResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        "The authenticated session could not be invalidated.");
                }
            }
        }

        await signInManager.SignOutAsync();
    }

    private AuditEvent CreateSessionAudit(
        Guid tenantId,
        Guid userId,
        string action,
        string correlationId) =>
        new(
            Guid.CreateVersion7(),
            tenantId,
            "User",
            userId.ToString(),
            action,
            "Session",
            userId.ToString(),
            correlationId,
            timeProvider.GetUtcNow());

    private static AuthSessionResponse CreateResponse(
        ApplicationUser user,
        MembershipSession membership) =>
        new(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            membership.TenantId,
            membership.TenantName,
            membership.Role.ToString());

    private static bool TryReadSession(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid tenantId)
    {
        userId = Guid.Empty;
        tenantId = Guid.Empty;
        return Guid.TryParse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier),
                out userId) &&
            userId != Guid.Empty &&
            Guid.TryParse(
                principal.FindFirstValue(TenantClaimTypes.TenantId),
                out tenantId) &&
            tenantId != Guid.Empty;
    }

    private sealed record MembershipSession(
        Guid TenantId,
        string TenantName,
        TenantStatus TenantStatus,
        TenantRole Role);

    private sealed record CurrentSession(
        ApplicationUser User,
        Guid TenantId,
        string TenantName,
        TenantRole Role);
}
