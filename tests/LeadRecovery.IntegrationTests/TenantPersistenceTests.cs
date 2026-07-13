using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class TenantPersistenceTests(LeadRecoveryApiFixture fixture)
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 13, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitialMigrationPersistsTenant()
    {
        Guid tenantId = Guid.CreateVersion7();
        Tenant tenant = CreateTenant(tenantId, $"tenant-{tenantId:N}");

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        dbContext.ChangeTracker.Clear();

        Tenant persistedTenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == tenantId,
            TestContext.Current.CancellationToken);

        Assert.Equal("Alpha Plumbing", persistedTenant.Name);
        Assert.Equal(TenantStatus.Trial, persistedTenant.Status);
        Assert.Equal(0, persistedTenant.Version);
    }

    [Fact]
    public async Task ConcurrentTenantUpdateIsRejected()
    {
        Guid tenantId = Guid.CreateVersion7();
        await PersistTenant(tenantId, $"tenant-{tenantId:N}");

        await using AsyncServiceScope firstScope = fixture.Application.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext firstContext =
            firstScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        LeadRecoveryDbContext secondContext =
            secondScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Tenant firstCopy = await firstContext.Tenants.SingleAsync(
            tenant => tenant.Id == tenantId,
            cancellationToken);
        Tenant secondCopy = await secondContext.Tenants.SingleAsync(
            tenant => tenant.Id == tenantId,
            cancellationToken);
        DateTimeOffset updatedAtUtc = CreatedAtUtc.AddMinutes(1);
        firstCopy.UpdateProfile("First Update", "America/Toronto", updatedAtUtc);
        secondCopy.UpdateProfile("Second Update", "America/Toronto", updatedAtUtc);

        await firstContext.SaveChangesAsync(cancellationToken);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync(cancellationToken));
        Assert.Equal(1, firstCopy.Version);
    }

    private async Task PersistTenant(Guid tenantId, string slug)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.Add(CreateTenant(tenantId, slug));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Tenant CreateTenant(Guid tenantId, string slug) =>
        new(tenantId, "Alpha Plumbing", slug, "America/Toronto", CreatedAtUtc);
}
