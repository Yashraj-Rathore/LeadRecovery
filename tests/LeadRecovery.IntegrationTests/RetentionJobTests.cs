using System.Text.Json;

using LeadRecovery.Application.Retention;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class RetentionJobTests(LeadRecoveryApiFixture fixture)
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 7, 28, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DryRunAndDeleteApplyOnlyTheActiveTenantPolicyAndAuditManifest()
    {
        Guid alphaTenantId = Guid.CreateVersion7();
        Guid betaTenantId = Guid.CreateVersion7();
        Guid alphaOldLeadId = Guid.CreateVersion7();
        Guid alphaRecentLeadId = Guid.CreateVersion7();
        Guid betaOldLeadId = Guid.CreateVersion7();
        await SeedTenantAndLeadsAsync(
            alphaTenantId,
            retentionDays: 30,
            alphaOldLeadId,
            alphaRecentLeadId);
        await SeedTenantAndLeadsAsync(
            betaTenantId,
            retentionDays: 365,
            betaOldLeadId,
            recentLeadId: null);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        IRetentionStore store = scope.ServiceProvider.GetRequiredService<IRetentionStore>();
        ITenantExecutionScope executionScope =
            scope.ServiceProvider.GetRequiredService<ITenantExecutionScope>();
        TenantRetentionPolicySnapshot alphaPolicy = Assert.Single(
            await store.ListEnabledPoliciesAsync(TestContext.Current.CancellationToken),
            policy => policy.TenantId == alphaTenantId);

        using (executionScope.Begin(betaTenantId))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ProcessTenantAsync(
                    alphaPolicy,
                    RetentionExecutionMode.Delete,
                    NowUtc.AddDays(-30),
                    batchSize: 100,
                    Guid.CreateVersion7(),
                    TestContext.Current.CancellationToken));
        }

        TenantRetentionResult preview;
        using (executionScope.Begin(alphaTenantId))
        {
            preview = await store.ProcessTenantAsync(
                alphaPolicy,
                RetentionExecutionMode.DryRun,
                NowUtc.AddDays(-alphaPolicy.RetentionDays),
                batchSize: 100,
                Guid.CreateVersion7(),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, preview.CandidateLeadCount);
        Assert.Equal(0, preview.DeletedLeadCount);
        Assert.True(await LeadExistsAsync(alphaOldLeadId));

        TenantRetentionResult deletion;
        using (executionScope.Begin(alphaTenantId))
        {
            deletion = await store.ProcessTenantAsync(
                alphaPolicy,
                RetentionExecutionMode.Delete,
                NowUtc.AddDays(-alphaPolicy.RetentionDays),
                batchSize: 100,
                Guid.CreateVersion7(),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, deletion.CandidateLeadCount);
        Assert.Equal(1, deletion.DeletedLeadCount);
        Assert.False(await LeadExistsAsync(alphaOldLeadId));
        Assert.True(await LeadExistsAsync(alphaRecentLeadId));
        Assert.True(await LeadExistsAsync(betaOldLeadId));

        LeadRecoveryDbContext verification =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        string[] actions = await verification.AuditEvents
            .Where(audit =>
                audit.TenantId == alphaTenantId &&
                audit.EntityType == "TenantRetention")
            .OrderBy(audit => audit.CreatedAtUtc)
            .Select(audit => audit.Action)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Retention.DryRun", "Retention.Deleted"], actions);
        string manifest = await verification.AuditEvents
            .Where(audit => audit.Id == deletion.AuditEventId)
            .Select(audit => audit.AfterJson!)
            .SingleAsync(TestContext.Current.CancellationToken);
        using JsonDocument manifestDocument = JsonDocument.Parse(manifest);
        Assert.Equal(
            1,
            manifestDocument.RootElement.GetProperty("deletedLeadCount").GetInt32());
        Assert.False(
            manifestDocument.RootElement.GetProperty("containsPersonalData").GetBoolean());
        Assert.DoesNotContain("+1", manifest, StringComparison.Ordinal);
    }

    private async Task SeedTenantAndLeadsAsync(
        Guid tenantId,
        int retentionDays,
        Guid oldLeadId,
        Guid? recentLeadId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        ITenantExecutionScope executionScope =
            scope.ServiceProvider.GetRequiredService<ITenantExecutionScope>();
        DateTimeOffset createdAtUtc = NowUtc.AddDays(-500);
        Tenant tenant = new(
            tenantId,
            $"Retention tenant {tenantId:N}",
            $"retention-{tenantId:N}",
            "America/Toronto",
            createdAtUtc);
        tenant.ConfigureDataRetention(true, retentionDays, createdAtUtc.AddSeconds(1));
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        using IDisposable tenantScope = executionScope.Begin(tenantId);
        Lead oldLead = CreateClosedLead(
            oldLeadId,
            tenantId,
            NowUtc.AddDays(-200),
            NowUtc.AddDays(-100));
        dbContext.Leads.Add(oldLead);
        if (recentLeadId is Guid id)
        {
            dbContext.Leads.Add(CreateClosedLead(
                id,
                tenantId,
                NowUtc.AddDays(-10),
                NowUtc.AddDays(-5)));
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> LeadExistsAsync(Guid leadId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        return await dbContext.Leads.IgnoreQueryFilters().AnyAsync(
            lead => lead.Id == leadId,
            TestContext.Current.CancellationToken);
    }

    private static Lead CreateClosedLead(
        Guid leadId,
        Guid tenantId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset closedAtUtc)
    {
        Lead lead = new(
            leadId,
            tenantId,
            "+14165550199",
            LeadSource.Manual,
            createdAtUtc);
        lead.Close(LeadCloseReason.LostNoResponse, closedAtUtc);
        return lead;
    }
}
