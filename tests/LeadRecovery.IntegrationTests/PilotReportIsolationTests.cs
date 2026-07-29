using System.Security.Claims;

using LeadRecovery.Application.Reporting;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class PilotReportIsolationTests(LeadRecoveryApiFixture fixture)
{
    [Fact]
    public async Task ReportCountsOnlyActiveTenantAuditEvents()
    {
        DateTimeOffset now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        Guid alphaId = Guid.CreateVersion7();
        Guid betaId = Guid.CreateVersion7();
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext = scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.AddRange(
            new Tenant(alphaId, "Pilot Alpha", $"pilot-alpha-{alphaId:N}", "America/Toronto", now),
            new Tenant(betaId, "Pilot Beta", $"pilot-beta-{betaId:N}", "America/Toronto", now));
        dbContext.AuditEvents.AddRange(
            OptOut(alphaId, now),
            OptOut(betaId, now.AddMinutes(1)),
            OptOut(betaId, now.AddMinutes(2)));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        PilotReportUseCase useCase = scope.ServiceProvider.GetRequiredService<PilotReportUseCase>();

        PilotReport alpha;
        using (new TenantClaimScope(scope.ServiceProvider, alphaId))
        {
            alpha = await useCase.ExecuteAsync(
                new DateOnly(2026, 7, 29),
                new DateOnly(2026, 7, 29),
                TestContext.Current.CancellationToken);
        }

        PilotReport beta;
        using (new TenantClaimScope(scope.ServiceProvider, betaId))
        {
            beta = await useCase.ExecuteAsync(
                new DateOnly(2026, 7, 29),
                new DateOnly(2026, 7, 29),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, alpha.OptOuts);
        Assert.Equal(2, beta.OptOuts);
    }

    private static AuditEvent OptOut(Guid tenantId, DateTimeOffset createdAtUtc) => new(
        Guid.CreateVersion7(),
        tenantId,
        "Integration",
        null,
        "CustomerSmsOptedOut",
        "Lead",
        Guid.CreateVersion7().ToString("N"),
        Guid.CreateVersion7().ToString("N"),
        createdAtUtc);

    private sealed class TenantClaimScope : IDisposable
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly HttpContext? _previous;

        public TenantClaimScope(IServiceProvider services, Guid tenantId)
        {
            _accessor = services.GetRequiredService<IHttpContextAccessor>();
            _previous = _accessor.HttpContext;
            ClaimsIdentity identity = new(
                [new Claim(TenantClaimTypes.TenantId, tenantId.ToString())],
                "PilotReportTest");
            _accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            };
        }

        public void Dispose() => _accessor.HttpContext = _previous;
    }
}
