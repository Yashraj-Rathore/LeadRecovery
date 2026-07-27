using System.Security.Claims;
using System.Text.Json;

using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Conversations;
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
            settings.AlphaProviderPhone,
            [(owner, TenantRole.Owner), (staff, TenantRole.Staff)],
            [
                new DemoLead(
                    settings.AlphaUrgentPhone,
                    "Urgent plumbing caller",
                    LeadSource.MissedCall,
                    LeadUrgency.CriticalReview,
                    AssignedUserId: null,
                    RequiresHumanReview: true,
                    IsQualified: false),
                new DemoLead(
                    settings.AlphaBookingPhone,
                    "Booking request",
                    LeadSource.InboundSms,
                    LeadUrgency.Normal,
                    AssignedUserId: owner.Id,
                    RequiresHumanReview: false,
                    IsQualified: true),
            ],
            now,
            cancellationToken);
        await EnsureTenantData(
            beta,
            settings.BetaProviderPhone,
            [(betaOwner, TenantRole.Owner)],
            [
                new DemoLead(
                    settings.BetaLeadPhone,
                    "Beta tenant lead",
                    LeadSource.Manual,
                    LeadUrgency.Low,
                    AssignedUserId: betaOwner.Id,
                    RequiresHumanReview: false,
                    IsQualified: false),
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
        string providerPhone,
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

            if (!tenant.AutomationEnabled)
            {
                tenant.SetAutomationEnabled(true, now);
            }

            await EnsureWorkflow(
                tenant,
                memberships.First().User.Id,
                now,
                cancellationToken);

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

            bool hasProviderNumber = await dbContext.TenantPhoneNumbers
                .IgnoreQueryFilters()
                .AnyAsync(
                    number =>
                        number.TenantId == tenant.Id &&
                        number.PhoneNumberE164 == providerPhone,
                    cancellationToken);
            if (!hasProviderNumber)
            {
                dbContext.TenantPhoneNumbers.Add(new TenantPhoneNumber(
                    Guid.CreateVersion7(),
                    tenant.Id,
                    "Twilio",
                    providerPhone,
                    $"PN{tenant.Id:N}",
                    ["busy", "failed", "no-answer"],
                    initialDelaySeconds: 60,
                    recoveryCooldownSeconds: 3600,
                    isPrimary: true));
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
                    if (item.Urgency != LeadUrgency.Unknown)
                    {
                        lead.ChangeUrgency(item.Urgency, createdAt.AddSeconds(10));
                    }

                    if (item.AssignedUserId is Guid assignedUserId)
                    {
                        lead.AssignTo(assignedUserId, createdAt.AddSeconds(20));
                    }

                    if (item.RequiresHumanReview)
                    {
                        lead.RequireHumanReview(createdAt.AddMinutes(1));
                    }

                    else if (item.IsQualified)
                    {
                        DateTimeOffset qualifiedAt = createdAt.AddMinutes(1);
                        lead.BeginContacting(qualifiedAt);
                        lead.AwaitCustomer(qualifiedAt);
                        lead.Qualify(true, null, qualifiedAt);
                    }

                    dbContext.Leads.Add(lead);
                    if (item.Source == LeadSource.MissedCall)
                    {
                        ScheduledAction action = new(
                            Guid.CreateVersion7(),
                            tenant.Id,
                            lead.Id,
                            ProcessCallStatusWebhookUseCase.RecoveryActionType,
                            now.AddMinutes(10),
                            $"demo-recovery:{lead.Id:N}",
                            JsonSerializer.Serialize(new { schemaVersion = 1 }),
                            createdAt.AddMinutes(1));
                        dbContext.ScheduledActions.Add(action);
                        dbContext.AuditEvents.Add(new AuditEvent(
                            Guid.CreateVersion7(),
                            tenant.Id,
                            "Integration",
                            "DemoTwilio",
                            "MissedCallRecoveryScheduled",
                            nameof(Lead),
                            lead.Id.ToString("N"),
                            $"demo:{lead.Id:N}",
                            createdAt.AddMinutes(1),
                            afterJson: JsonSerializer.Serialize(new
                            {
                                result = "RecoveryScheduled",
                                scheduledActionId = action.Id,
                            })));
                    }

                    if (item.Source == LeadSource.InboundSms)
                    {
                        DateTimeOffset messageAt = createdAt.AddMinutes(2);
                        Conversation conversation = new(
                            Guid.CreateVersion7(),
                            tenant.Id,
                            lead.Id,
                            ConversationChannel.Sms,
                            createdAt.AddMinutes(1));
                        Message message = Message.ReceiveInbound(
                            Guid.CreateVersion7(),
                            tenant.Id,
                            lead.Id,
                            conversation.Id,
                            MessageKind.Manual,
                            "Twilio",
                            $"SM{lead.Id:N}",
                            $"demo-inbound:{lead.Id:N}",
                            "Could someone help me schedule a service visit?",
                            messageAt);
                        lead.RecordCustomerActivity(messageAt);
                        dbContext.Conversations.Add(conversation);
                        dbContext.Messages.Add(message);
                        Guid noteAuthor = item.AssignedUserId ?? memberships.First().User.Id;
                        dbContext.LeadNotes.Add(new LeadNote(
                            Guid.CreateVersion7(),
                            tenant.Id,
                            lead.Id,
                            noteAuthor,
                            "Customer prefers an afternoon appointment.",
                            messageAt.AddMinutes(1)));
                    }

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

    private async Task EnsureWorkflow(
        Tenant tenant,
        Guid authorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool hasActiveWorkflow = await dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AnyAsync(
                workflow => workflow.TenantId == tenant.Id && workflow.IsActive,
                cancellationToken);
        if (!hasActiveWorkflow)
        {
            WorkflowDefinition workflow = new(
                Guid.CreateVersion7(),
                tenant.Id,
                "Default demo recovery workflow",
                1,
                $"https://booking.example.test/{tenant.Slug}",
                [
                    new QualificationQuestionPolicy(
                        "service",
                        "Which service do you need?",
                        QualificationAnswerKind.Choice,
                        ["Plumbing", "HVAC", "Electrical"]),
                    new QualificationQuestionPolicy(
                        "problem",
                        "Briefly describe the problem.",
                        QualificationAnswerKind.RequiredText,
                        []),
                ],
                new BusinessHoursPolicy(
                    [
                        new(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
                        new(DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
                        new(DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
                        new(DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
                        new(DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
                    ],
                    true),
                [
                    new FollowUpStepPolicy(1, 60, "WorkflowFollowUpOne"),
                    new FollowUpStepPolicy(2, 1440, "WorkflowFollowUpTwo"),
                ],
                now);
            workflow.Activate(now);
            dbContext.WorkflowDefinitions.Add(workflow);
        }

        await EnsureTemplate(
            tenant.Id,
            authorUserId,
            "Booking link",
            SmsTemplatePurposes.BookingLink,
            "Choose a service time here: {{BookingUrl}}",
            now,
            cancellationToken);
        await EnsureTemplate(
            tenant.Id,
            authorUserId,
            "Workflow follow-up one",
            "WorkflowFollowUpOne",
            "Are you still looking for help from {{BusinessName}}?",
            now,
            cancellationToken);
        await EnsureTemplate(
            tenant.Id,
            authorUserId,
            "Workflow follow-up two",
            "WorkflowFollowUpTwo",
            "Reply if you would still like our team to contact you.",
            now,
            cancellationToken);
    }

    private async Task EnsureTemplate(
        Guid tenantId,
        Guid authorUserId,
        string name,
        string purpose,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool exists = await dbContext.MessageTemplates.IgnoreQueryFilters().AnyAsync(
            template => template.TenantId == tenantId &&
                template.Purpose == purpose &&
                template.IsActive,
            cancellationToken);
        if (exists)
        {
            return;
        }

        MessageTemplate template = new(
            Guid.CreateVersion7(),
            tenantId,
            name,
            purpose,
            body,
            1,
            authorUserId,
            now);
        template.Approve(authorUserId, now);
        template.Activate();
        dbContext.MessageTemplates.Add(template);
    }

    private sealed record DemoLead(
        string Phone,
        string DisplayName,
        LeadSource Source,
        LeadUrgency Urgency,
        Guid? AssignedUserId,
        bool RequiresHumanReview,
        bool IsQualified);
}
