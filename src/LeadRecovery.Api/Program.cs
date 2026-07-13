using LeadRecovery.Api.Tenancy;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Infrastructure;
using LeadRecovery.Infrastructure.Persistence;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

string databaseConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "The ConnectionStrings:Database configuration value is required.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddInfrastructure(databaseConnectionString);
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<LeadRecoveryDbContext>(
        "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = static _ => false,
    });
app.MapHealthChecks("/health/ready");

await app.RunAsync();

public partial class Program;
