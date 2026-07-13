using System.Security.Claims;

using LeadRecovery.Application.Tenancy;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class TenantContextTests(LeadRecoveryApiFixture fixture)
{
    [Fact]
    public void TenantContextUsesServerIssuedClaim()
    {
        Guid expectedTenantId = Guid.CreateVersion7();
        using IServiceScope scope = fixture.Application.Services.CreateScope();
        IHttpContextAccessor accessor =
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = CreateHttpContext(
            new Claim(TenantClaimTypes.TenantId, expectedTenantId.ToString()));

        try
        {
            ITenantContext tenantContext =
                scope.ServiceProvider.GetRequiredService<ITenantContext>();

            Assert.Equal(expectedTenantId, tenantContext.TenantId);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Fact]
    public void MissingTenantClaimFailsClosedEvenWhenRequestSuppliesTenantValues()
    {
        Guid untrustedTenantId = Guid.CreateVersion7();
        using IServiceScope scope = fixture.Application.Services.CreateScope();
        IHttpContextAccessor accessor =
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        DefaultHttpContext httpContext = CreateHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = untrustedTenantId.ToString();
        httpContext.Request.QueryString = new QueryString($"?tenantId={untrustedTenantId}");
        accessor.HttpContext = httpContext;

        try
        {
            ITenantContext tenantContext =
                scope.ServiceProvider.GetRequiredService<ITenantContext>();

            Assert.Throws<TenantContextUnavailableException>(() => tenantContext.TenantId);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void InvalidTenantClaimFailsClosed(string tenantClaimValue)
    {
        using IServiceScope scope = fixture.Application.Services.CreateScope();
        IHttpContextAccessor accessor =
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = CreateHttpContext(
            new Claim(TenantClaimTypes.TenantId, tenantClaimValue));

        try
        {
            ITenantContext tenantContext =
                scope.ServiceProvider.GetRequiredService<ITenantContext>();

            Assert.Throws<TenantContextUnavailableException>(() => tenantContext.TenantId);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static DefaultHttpContext CreateHttpContext(params Claim[] claims)
    {
        ClaimsIdentity identity = new(claims, "IntegrationTest");
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };
    }
}
