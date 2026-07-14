using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

using LeadRecovery.Application.Authorization;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Contracts.Authentication;
using LeadRecovery.Contracts.Leads;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class AuthenticationAuthorizationTests(LeadRecoveryApiFixture fixture)
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 14, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Staff)]
    public async Task OwnerAndStaffCanAuthenticate(TenantRole role)
    {
        TenantData tenant = await SeedTenant();
        UserData user = await SeedUser(tenant, role, createLead: false);
        using HttpClient client = CreateClient();

        LoginResult login = await Login(client, user);
        AuthSessionResponse? current = await client.GetFromJsonAsync<AuthSessionResponse>(
            "/api/v1/auth/me",
            TestContext.Current.CancellationToken);

        Assert.Equal(user.UserId, login.Session.UserId);
        Assert.Equal(tenant.TenantId, login.Session.TenantId);
        Assert.Equal(role.ToString(), login.Session.Role);
        Assert.NotNull(current);
        Assert.Equal(login.Session, current);
        Assert.Contains("HttpOnly", login.SessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", login.SessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", login.SessionCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LogoutInvalidatesPreviouslyIssuedCookieAndRecordsAudit()
    {
        TenantData tenant = await SeedTenant();
        UserData user = await SeedUser(tenant, TenantRole.Owner, createLead: false);
        using HttpClient client = CreateClient();
        LoginResult login = await Login(client, user);
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage logoutRequest = new(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);

        using HttpResponseMessage logoutResponse = await client.SendAsync(
            logoutRequest,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage currentResponse = await client.GetAsync(
            "/api/v1/auth/me",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, currentResponse.StatusCode);

        using HttpClient replayClient = CreateClient(handleCookies: false);
        replayClient.DefaultRequestHeaders.Add(
            "Cookie",
            login.SessionCookie[..login.SessionCookie.IndexOf(';')]);
        using HttpResponseMessage replayResponse = await replayClient.GetAsync(
            "/api/v1/auth/me",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        string[] actions = await dbContext.AuditEvents
            .Where(auditEvent => auditEvent.TenantId == tenant.TenantId)
            .OrderBy(auditEvent => auditEvent.CreatedAtUtc)
            .Select(auditEvent => auditEvent.Action)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Authentication.Login", "Authentication.Logout"], actions);
    }

    [Fact]
    public async Task LoginAndLogoutRequireAntiforgeryTokens()
    {
        TenantData tenant = await SeedTenant();
        UserData user = await SeedUser(tenant, TenantRole.Owner, createLead: false);
        using HttpClient client = CreateClient();

        using HttpResponseMessage loginWithoutToken = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(user.Email, user.Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, loginWithoutToken.StatusCode);

        _ = await Login(client, user);
        using HttpResponseMessage logoutWithoutToken = await client.PostAsync(
            "/api/v1/auth/logout",
            content: null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, logoutWithoutToken.StatusCode);
    }

    [Fact]
    public async Task LeadEndpointsRequireAuthenticationAndDenyCrossTenantAccess()
    {
        TenantData alpha = await SeedTenant();
        TenantData beta = await SeedTenant();
        UserData alphaOwner = await SeedUser(alpha, TenantRole.Owner, createLead: true);
        UserData betaOwner = await SeedUser(beta, TenantRole.Owner, createLead: true);
        Assert.NotNull(alphaOwner.LeadId);
        Assert.NotNull(betaOwner.LeadId);

        using HttpClient anonymousClient = CreateClient();
        using HttpResponseMessage anonymousResponse = await anonymousClient.GetAsync(
            "/api/v1/leads/",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using HttpClient alphaClient = CreateClient();
        _ = await Login(alphaClient, alphaOwner);
        LeadPageResponse? page = await alphaClient.GetFromJsonAsync<LeadPageResponse>(
            "/api/v1/leads/",
            TestContext.Current.CancellationToken);
        Assert.NotNull(page);
        LeadSummaryResponse onlyLead = Assert.Single(page.Items);
        Assert.Equal(alphaOwner.LeadId, onlyLead.Id);

        using HttpRequestMessage crossTenantRequest = new(
            HttpMethod.Get,
            $"/api/v1/leads/{betaOwner.LeadId}");
        crossTenantRequest.Headers.Add("X-Tenant-Id", beta.TenantId.ToString());
        using HttpResponseMessage crossTenantResponse = await alphaClient.SendAsync(
            crossTenantRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
    }

    [Fact]
    public async Task SuspendedTenantAndInvalidCredentialsUseGenericFailure()
    {
        TenantData tenant = await SeedTenant(TenantStatus.Suspended);
        UserData user = await SeedUser(tenant, TenantRole.Owner, createLead: false);
        using HttpClient client = CreateClient();

        using HttpResponseMessage suspendedResponse = await SendLogin(
            client,
            user.Email,
            user.Password);
        using HttpResponseMessage invalidResponse = await SendLogin(
            client,
            user.Email,
            $"Aa1!{Guid.CreateVersion7():N}");

        Assert.Equal(HttpStatusCode.Unauthorized, suspendedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        Assert.Contains(
            "Authentication failed",
            await suspendedResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Contains(
            "Authentication failed",
            await invalidResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipleActiveMembershipsFailClosedUntilTenantSelectionExists()
    {
        TenantData alpha = await SeedTenant();
        TenantData beta = await SeedTenant();
        UserData user = await SeedUser(alpha, TenantRole.Owner, createLead: false);
        await AddMembership(user, beta, TenantRole.Staff);
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await SendLogin(
            client,
            user.Email,
            user.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            "Authentication failed",
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnerPolicyAllowsOwnerAndDeniesStaff()
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        IAuthorizationService authorization =
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal owner = CreatePrincipal(TenantRole.Owner);
        ClaimsPrincipal staff = CreatePrincipal(TenantRole.Staff);

        AuthorizationResult ownerResult = await authorization.AuthorizeAsync(
            owner,
            resource: null,
            AuthorizationPolicies.OwnerOnly);
        AuthorizationResult staffResult = await authorization.AuthorizeAsync(
            staff,
            resource: null,
            AuthorizationPolicies.OwnerOnly);

        Assert.True(ownerResult.Succeeded);
        Assert.False(staffResult.Succeeded);
    }

    private HttpClient CreateClient(bool handleCookies = true) =>
        fixture.Application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
        });

    private async Task<TenantData> SeedTenant(
        TenantStatus status = TenantStatus.Trial)
    {
        Guid tenantId = Guid.CreateVersion7();
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Tenant tenant = new(
            tenantId,
            $"Tenant {tenantId:N}",
            $"tenant-{tenantId:N}",
            "America/Toronto",
            CreatedAtUtc);
        if (status != TenantStatus.Trial)
        {
            tenant.ChangeStatus(status, CreatedAtUtc.AddSeconds(1));
        }

        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new TenantData(tenantId);
    }

    private async Task<UserData> SeedUser(
        TenantData tenant,
        TenantRole role,
        bool createLead)
    {
        Guid userId = Guid.CreateVersion7();
        string email = $"{userId:N}@example.test";
        string password = $"Aa1!{Guid.CreateVersion7():N}";
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        UserManager<ApplicationUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        ApplicationUser user = new(userId, email, $"Test {role}", CreatedAtUtc)
        {
            EmailConfirmed = true,
        };
        IdentityResult creation = await userManager.CreateAsync(user, password);
        Assert.True(
            creation.Succeeded,
            string.Join(", ", creation.Errors.Select(error => error.Code)));

        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenant.TenantId);
        dbContext.TenantMemberships.Add(new TenantMembership(
            Guid.CreateVersion7(),
            tenant.TenantId,
            userId,
            role,
            CreatedAtUtc));
        Guid? leadId = null;
        if (createLead)
        {
            leadId = Guid.CreateVersion7();
            dbContext.Leads.Add(new Lead(
                leadId.Value,
                tenant.TenantId,
                CreatePhone(tenant.TenantId),
                LeadSource.Manual,
                CreatedAtUtc,
                $"Lead {tenant.TenantId:N}"));
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new UserData(userId, email, password, leadId);
    }

    private async Task AddMembership(
        UserData user,
        TenantData tenant,
        TenantRole role)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenant.TenantId);
        dbContext.TenantMemberships.Add(new TenantMembership(
            Guid.CreateVersion7(),
            tenant.TenantId,
            user.UserId,
            role,
            CreatedAtUtc));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<LoginResult> Login(HttpClient client, UserData user)
    {
        using HttpResponseMessage response = await SendLogin(
            client,
            user.Email,
            user.Password);
        response.EnsureSuccessStatusCode();
        AuthSessionResponse? session = await response.Content
            .ReadFromJsonAsync<AuthSessionResponse>(TestContext.Current.CancellationToken);
        string sessionCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                "leadrecovery.session=",
                StringComparison.OrdinalIgnoreCase));
        return new LoginResult(Assert.IsType<AuthSessionResponse>(session), sessionCookie);
    }

    private static async Task<HttpResponseMessage> SendLogin(
        HttpClient client,
        string email,
        string password)
    {
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password)),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetCsrfToken(HttpClient client)
    {
        CsrfTokenResponse? response = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/api/v1/auth/csrf",
            TestContext.Current.CancellationToken);
        return Assert.IsType<CsrfTokenResponse>(response).Token;
    }

    private static ClaimsPrincipal CreatePrincipal(TenantRole role)
    {
        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
                new Claim(TenantClaimTypes.TenantId, Guid.CreateVersion7().ToString()),
                new Claim(ClaimTypes.Role, role.ToString()),
            ],
            "TestCookie");
        return new ClaimsPrincipal(identity);
    }

    private static string CreatePhone(Guid tenantId)
    {
        byte[] bytes = tenantId.ToByteArray();
        int suffix = BitConverter.ToUInt16(bytes, 0) % 10_000;
        return $"+1416555{suffix:D4}";
    }

    private sealed record TenantData(Guid TenantId);

    private sealed record UserData(
        Guid UserId,
        string Email,
        string Password,
        Guid? LeadId);

    private sealed record LoginResult(
        AuthSessionResponse Session,
        string SessionCookie);

    private sealed class TenantClaimScope : IDisposable
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly HttpContext? _previousContext;

        public TenantClaimScope(IServiceProvider services, Guid tenantId)
        {
            _accessor = services.GetRequiredService<IHttpContextAccessor>();
            _previousContext = _accessor.HttpContext;
            ClaimsIdentity identity = new(
                [new Claim(TenantClaimTypes.TenantId, tenantId.ToString())],
                "IntegrationTest");
            _accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            };
        }

        public void Dispose()
        {
            _accessor.HttpContext = _previousContext;
        }
    }
}
