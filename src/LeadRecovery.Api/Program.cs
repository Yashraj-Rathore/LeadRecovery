using System.Threading.RateLimiting;

using LeadRecovery.Api.Demo;
using LeadRecovery.Api.Endpoints;
using LeadRecovery.Api.Identity;
using LeadRecovery.Api.Integrations.Twilio;
using LeadRecovery.Api.Middleware;
using LeadRecovery.Api.Tenancy;
using LeadRecovery.Application.Analysis;
using LeadRecovery.Application.Authorization;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Infrastructure;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Persistence;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

string databaseConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "The ConnectionStrings:Database configuration value is required.");

bool aiEnabled = builder.Configuration.GetValue<bool?>("AI_ENABLED") ??
    builder.Configuration.GetValue("Ai:Enabled", false);
string aiCategoryQuestionKey = builder.Configuration["AI_CATEGORY_QUESTION_KEY"] ??
    builder.Configuration["Ai:CategoryQuestionKey"] ??
    LeadAnalysisWorkflowOptions.DefaultCategoryQuestionKey;

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantContext>(services =>
    services.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<ITenantExecutionScope>(services =>
    services.GetRequiredService<HttpTenantContext>());
builder.Services.AddInfrastructure(
    databaseConnectionString,
    new LeadAnalysisWorkflowOptions(aiEnabled, aiCategoryQuestionKey));
builder.Services.AddTwilioCallIngestion(builder.Configuration["TWILIO_AUTH_TOKEN"]);
builder.Services.AddSingleton(new TwilioWebhookOptions(
    builder.Configuration["TWILIO_WEBHOOK_BASE_URL"],
    builder.Environment.IsDevelopment()));
builder.Services.AddScoped<TwilioCallStatusRequestAdapter>();
builder.Services.AddScoped<TwilioSmsRequestAdapter>();
builder.Services.AddScoped<SignInManager<ApplicationUser>>();
builder.Services.AddScoped<AuthenticationSessionService>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddProblemDetails();

string cookieName = builder.Configuration["AUTH_COOKIE_NAME"] ??
    (builder.Environment.IsDevelopment()
        ? "leadrecovery.session"
        : "__Host-LeadRecovery.Session");
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(
        IdentityConstants.ApplicationScheme,
        options =>
        {
            options.Cookie.Name = cookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                },
                OnValidatePrincipal = CookieSessionValidator.ValidateAsync,
            };
        });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(
        AuthorizationPolicies.TenantMember,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaimTypes.TenantId)
            .RequireRole(
                TenantRole.Owner.ToString(),
                TenantRole.Manager.ToString(),
                TenantRole.Staff.ToString(),
                TenantRole.ReadOnly.ToString()))
    .AddPolicy(
        AuthorizationPolicies.DashboardOperator,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaimTypes.TenantId)
            .RequireRole(
                TenantRole.Owner.ToString(),
                TenantRole.Manager.ToString(),
                TenantRole.Staff.ToString()))
    .AddPolicy(
        AuthorizationPolicies.OwnerOnly,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaimTypes.TenantId)
            .RequireRole(TenantRole.Owner.ToString()));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = builder.Environment.IsDevelopment()
        ? "leadrecovery.antiforgery"
        : "__Host-LeadRecovery.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    int loginPermitLimit = builder.Configuration.GetValue(
        "RateLimiting:LoginPermitLimit",
        5);
    if (loginPermitLimit < 1)
    {
        throw new InvalidOperationException(
            "RateLimiting:LoginPermitLimit must be greater than zero.");
    }

    int manualMessagePermitLimit = builder.Configuration.GetValue(
        "RateLimiting:ManualMessagePermitLimit",
        10);
    if (manualMessagePermitLimit < 1)
    {
        throw new InvalidOperationException(
            "RateLimiting:ManualMessagePermitLimit must be greater than zero.");
    }

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "login",
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
            }));
    options.AddPolicy(
        "manual-message",
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                context.Connection.RemoteIpAddress?.ToString() ??
                "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = manualMessagePermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
            }));
});

string? dataProtectionKeyPath = builder.Configuration["DATA_PROTECTION_KEY_PATH"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
        .SetApplicationName("LeadRecovery");
}
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<LeadRecoveryDbContext>(
        "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = static _ => false,
    });
app.MapHealthChecks("/health/ready");
app.MapAuthenticationEndpoints();
app.MapLeadEndpoints();
app.MapTwilioWebhookEndpoints();

await app.Services.SeedDemoDataAsync(
    app.Configuration,
    app.Lifetime.ApplicationStopping);

await app.RunAsync();

public partial class Program;
