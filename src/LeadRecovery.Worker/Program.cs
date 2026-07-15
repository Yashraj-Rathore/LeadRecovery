using Hangfire;

using LeadRecovery.Application.Tenancy;
using LeadRecovery.Infrastructure;
using LeadRecovery.Infrastructure.BackgroundJobs;
using LeadRecovery.Infrastructure.Messaging;
using LeadRecovery.Worker;

var builder = Host.CreateApplicationBuilder(args);
string databaseConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "The ConnectionStrings:Database configuration value is required.");
string webhookBaseUrl = builder.Configuration["TWILIO_WEBHOOK_BASE_URL"]
    ?? throw new InvalidOperationException(
        "TWILIO_WEBHOOK_BASE_URL is required for delivery callbacks.");
if (!Uri.TryCreate(webhookBaseUrl.TrimEnd('/'), UriKind.Absolute, out Uri? baseUri))
{
    throw new InvalidOperationException("TWILIO_WEBHOOK_BASE_URL must be an absolute URL.");
}

builder.Services.AddScoped<BackgroundTenantContext>();
builder.Services.AddScoped<ITenantContext>(services =>
    services.GetRequiredService<BackgroundTenantContext>());
builder.Services.AddScoped<ITenantExecutionScope>(services =>
    services.GetRequiredService<BackgroundTenantContext>());
builder.Services.AddInfrastructure(databaseConnectionString);
builder.Services.AddLeadRecoveryHangfire(databaseConnectionString);
builder.Services.AddSmsProvider(new SmsProviderOptions(
    builder.Configuration["SMS_PROVIDER"] ?? "fake",
    builder.Configuration.GetValue("ALLOW_REAL_SMS", false),
    builder.Configuration["TWILIO_ACCOUNT_SID"],
    builder.Configuration["TWILIO_AUTH_TOKEN"]));
builder.Services.AddSingleton(new SmsWorkerOptions(
    new Uri(baseUri, "/api/v1/webhooks/twilio/sms/status"),
    TimeSpan.FromSeconds(1),
    TimeSpan.FromMinutes(5)));
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["sms"];
    options.WorkerCount = builder.Configuration.GetValue("SMS_WORKER_COUNT", 2);
});
builder.Services.AddHostedService<ScheduledActionDispatcher>();

var host = builder.Build();
await host.RunAsync();
