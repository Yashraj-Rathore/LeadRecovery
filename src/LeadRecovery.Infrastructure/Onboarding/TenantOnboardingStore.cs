using System.Data;
using System.Text.Json;

using LeadRecovery.Application.Onboarding;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Persistence;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeadRecovery.Infrastructure.Onboarding;

internal sealed class TenantOnboardingStore(
    LeadRecoveryDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ITenantExecutionScope tenantExecutionScope,
    TimeProvider timeProvider)
    : ITenantOnboardingStore
{
    public async Task<TenantOnboardingResult> ProvisionAsync(
        ValidatedTenantOnboardingPlan plan,
        IReadOnlyDictionary<string, string> userPasswords,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(userPasswords);

        TenantOnboardingResult? conflict = await FindConflictAsync(plan, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid tenantId = Guid.CreateVersion7();
        using IDisposable tenantScope = tenantExecutionScope.Begin(tenantId);
        Tenant tenant = new(
            tenantId,
            plan.Business.Name,
            plan.Business.Slug,
            plan.Business.TimezoneId,
            now);
        dbContext.Tenants.Add(tenant);

        Dictionary<string, ApplicationUser> users = new(StringComparer.OrdinalIgnoreCase);
        foreach (ValidatedTenantOnboardingUser configuredUser in plan.Users)
        {
            ApplicationUser user = new(
                Guid.CreateVersion7(),
                configuredUser.Email,
                configuredUser.DisplayName,
                now)
            {
                EmailConfirmed = true,
            };
            IdentityResult identityResult = await userManager.CreateAsync(
                user,
                userPasswords[configuredUser.Email]);
            if (!identityResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return TenantOnboardingResult.ValidationFailed(
                    identityResult.Errors
                        .Select(error => new TenantOnboardingValidationError(
                            $"users[{configuredUser.Email}].password",
                            error.Description))
                        .ToArray());
            }

            users[configuredUser.Email] = user;
            dbContext.TenantMemberships.Add(new TenantMembership(
                Guid.CreateVersion7(),
                tenantId,
                user.Id,
                configuredUser.Role,
                now));
        }

        ApplicationUser owner = users[plan.Users.Single(user =>
            user.Role == TenantRole.Owner).Email];
        dbContext.TenantPhoneNumbers.Add(new TenantPhoneNumber(
            Guid.CreateVersion7(),
            tenantId,
            plan.Phone.Provider,
            plan.Phone.PhoneNumberE164,
            plan.Phone.ProviderNumberSid,
            plan.Phone.RecoverableCallStatuses,
            plan.Phone.InitialDelaySeconds,
            plan.Phone.RecoveryCooldownSeconds,
            plan.Phone.InboundSmsEnabled,
            plan.Phone.MissedCallRecoveryEnabled,
            isPrimary: true));

        WorkflowDefinition workflow = new(
            Guid.CreateVersion7(),
            tenantId,
            plan.Workflow.Name,
            1,
            plan.Workflow.BookingUrl,
            plan.QualificationQuestions.ToArray(),
            plan.BusinessHours,
            plan.FollowUps.ToArray(),
            now);
        workflow.Activate(now);
        dbContext.WorkflowDefinitions.Add(workflow);

        foreach (TenantOnboardingTemplate configuredTemplate in plan.Templates)
        {
            MessageTemplate template = new(
                Guid.CreateVersion7(),
                tenantId,
                configuredTemplate.Name,
                configuredTemplate.Purpose,
                configuredTemplate.Body,
                1,
                owner.Id,
                now);
            template.Approve(owner.Id, now);
            template.Activate();
            dbContext.MessageTemplates.Add(template);
        }

        tenant.ConfigureDataRetention(
            plan.Retention.Enabled,
            plan.Retention.Days,
            now);
        tenant.SetAutomationEnabled(plan.EnableAutomation, now);
        tenant.ChangeStatus(TenantStatus.Active, now);
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.CreateVersion7(),
            tenantId,
            "PlatformOperator",
            null,
            "Tenant.OnboardingActivated",
            nameof(Tenant),
            tenantId.ToString("N"),
            $"onboarding:{tenantId:N}",
            now,
            afterJson: JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                phoneCount = 1,
                workflowVersion = 1,
                templateCount = plan.Templates.Count,
                userCount = plan.Users.Count,
                automationEnabled = plan.EnableAutomation,
                retentionEnabled = plan.Retention.Enabled,
                retentionDays = plan.Retention.Days,
                containsPersonalData = false,
            })));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TenantOnboardingResult.Activated(tenantId);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TenantOnboardingResult.Conflict(
                "plan",
                "The onboarding plan conflicts with existing tenant, phone, or user data.");
        }
    }

    private async Task<TenantOnboardingResult?> FindConflictAsync(
        ValidatedTenantOnboardingPlan plan,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Tenants.AnyAsync(
                tenant => tenant.Slug == plan.Business.Slug,
                cancellationToken))
        {
            return TenantOnboardingResult.Conflict(
                "business.slug",
                "The tenant slug is already in use.");
        }

        if (await dbContext.TenantPhoneNumbers.IgnoreQueryFilters().AnyAsync(
                number =>
                    number.Provider == plan.Phone.Provider &&
                    (number.PhoneNumberE164 == plan.Phone.PhoneNumberE164 ||
                        number.ProviderNumberSid == plan.Phone.ProviderNumberSid),
                cancellationToken))
        {
            return TenantOnboardingResult.Conflict(
                "phone",
                "The provider phone number or provider number ID is already in use.");
        }

        foreach (ValidatedTenantOnboardingUser user in plan.Users)
        {
            if (await userManager.FindByEmailAsync(user.Email) is not null)
            {
                return TenantOnboardingResult.Conflict(
                    $"users[{user.Email}].email",
                    "The user email is already in use.");
            }
        }

        return null;
    }
}
