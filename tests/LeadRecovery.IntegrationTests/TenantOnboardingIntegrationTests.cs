using LeadRecovery.Application.Onboarding;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class TenantOnboardingIntegrationTests(LeadRecoveryApiFixture fixture)
{
    [Fact]
    public async Task CompletePlanActivatesEveryRequiredTenantRecord()
    {
        string key = Guid.NewGuid().ToString("N");
        TenantOnboardingPlan plan = CreatePlan(key);
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        ITenantOnboardingStore store = scope.ServiceProvider.GetRequiredService<ITenantOnboardingStore>();
        TenantOnboardingUseCase useCase = new(store, new FixedSecrets("Valid!OnboardingPassword123"));

        TenantOnboardingResult result = await useCase.ExecuteAsync(plan, TestContext.Current.CancellationToken);

        Guid tenantId = Assert.IsType<Guid>(result.TenantId);
        Assert.Equal(TenantOnboardingStatus.Activated, result.Status);
        LeadRecoveryDbContext dbContext = scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Tenant tenant = await dbContext.Tenants.SingleAsync(item => item.Id == tenantId, TestContext.Current.CancellationToken);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.False(tenant.AutomationEnabled);
        Assert.True(tenant.DataRetentionEnabled);
        Assert.Equal(180, tenant.DataRetentionDays);
        Assert.Equal(1, await dbContext.TenantPhoneNumbers.IgnoreQueryFilters().CountAsync(item => item.TenantId == tenantId, TestContext.Current.CancellationToken));
        Assert.Equal(1, await dbContext.WorkflowDefinitions.IgnoreQueryFilters().CountAsync(item => item.TenantId == tenantId && item.IsActive, TestContext.Current.CancellationToken));
        Assert.Equal(3, await dbContext.MessageTemplates.IgnoreQueryFilters().CountAsync(item => item.TenantId == tenantId && item.IsActive, TestContext.Current.CancellationToken));
        Assert.Equal(1, await dbContext.TenantMemberships.IgnoreQueryFilters().CountAsync(item => item.TenantId == tenantId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdentityFailureRollsBackTenantActivationAndUser()
    {
        string key = Guid.NewGuid().ToString("N");
        TenantOnboardingPlan plan = CreatePlan(key);
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        ITenantOnboardingStore store = scope.ServiceProvider.GetRequiredService<ITenantOnboardingStore>();
        TenantOnboardingUseCase useCase = new(store, new FixedSecrets("weak"));

        TenantOnboardingResult result = await useCase.ExecuteAsync(plan, TestContext.Current.CancellationToken);

        Assert.Equal(TenantOnboardingStatus.ValidationFailed, result.Status);
        LeadRecoveryDbContext dbContext = scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.False(await dbContext.Tenants.AnyAsync(item => item.Slug == $"onboarding-{key}", TestContext.Current.CancellationToken));
        Assert.False(await dbContext.Users.AnyAsync(item => item.Email == $"owner-{key}@example.test", TestContext.Current.CancellationToken));
    }

    private static TenantOnboardingPlan CreatePlan(string key) => new(
        1,
        new("Onboarding Test Business", $"onboarding-{key}", "America/Toronto"),
        new("Twilio", $"+1416{key[..7].Replace('a', '2').Replace('b', '3').Replace('c', '4').Replace('d', '5').Replace('e', '6').Replace('f', '7')}", $"PN{key}", ["busy", "failed", "no-answer"], 60, 3600),
        new(
            "Test workflow",
            $"https://booking.example.test/{key}",
            [new("problem", "Describe the problem.", "RequiredText", [])],
            [new("Monday", "08:00", "18:00")],
            [new(1, 60, "FollowUpOne")]),
        [
            new("Recovery", "InitialMissedCallRecovery", "Sorry we missed your call. Reply STOP to opt out."),
            new("Booking", "BookingLink", "Book here: {{BookingUrl}}"),
            new("Follow-up", "FollowUpOne", "Are you still looking for help?"),
        ],
        [new($"owner-{key}@example.test", "Test Owner", "Owner", "TEST_OWNER_PASSWORD")],
        EnableAutomation: false,
        Retention: new(true, 180));

    private sealed class FixedSecrets(string password) : IOnboardingSecretSource
    {
        public string? GetSecret(string name) => password;
    }
}
