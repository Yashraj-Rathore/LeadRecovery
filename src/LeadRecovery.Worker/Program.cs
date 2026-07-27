using System.Diagnostics;

using Hangfire;

using LeadRecovery.Application.Analysis;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Infrastructure;
using LeadRecovery.Infrastructure.Analysis;
using LeadRecovery.Infrastructure.BackgroundJobs;
using LeadRecovery.Infrastructure.Messaging;
using LeadRecovery.Infrastructure.Observability;
using LeadRecovery.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.Configure(options =>
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId |
        ActivityTrackingOptions.SpanId |
        ActivityTrackingOptions.ParentId);
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});
string? configuredLogLevel = builder.Configuration["LOG_LEVEL"];
if (!string.IsNullOrWhiteSpace(configuredLogLevel))
{
    if (!Enum.TryParse(configuredLogLevel, ignoreCase: true, out LogLevel minimumLogLevel) ||
        !Enum.IsDefined(minimumLogLevel))
    {
        throw new InvalidOperationException("LOG_LEVEL must be a valid .NET log level.");
    }

    builder.Logging.SetMinimumLevel(minimumLogLevel);
}
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

bool aiEnabled = builder.Configuration.GetValue<bool?>("AI_ENABLED") ??
    builder.Configuration.GetValue("Ai:Enabled", false);
string aiCategoryQuestionKey = builder.Configuration["AI_CATEGORY_QUESTION_KEY"] ??
    builder.Configuration["Ai:CategoryQuestionKey"] ??
    LeadAnalysisWorkflowOptions.DefaultCategoryQuestionKey;

builder.Services.AddScoped<BackgroundTenantContext>();
builder.Services.AddScoped<ITenantContext>(services =>
    services.GetRequiredService<BackgroundTenantContext>());
builder.Services.AddScoped<ITenantExecutionScope>(services =>
    services.GetRequiredService<BackgroundTenantContext>());
builder.Services.AddInfrastructure(
    databaseConnectionString,
    new LeadAnalysisWorkflowOptions(aiEnabled, aiCategoryQuestionKey));
builder.Services.AddLeadRecoveryObservability(
    builder.Configuration,
    "LeadRecovery.Worker",
    builder.Environment.EnvironmentName,
    instrumentAspNetCore: false);
builder.Services.AddLeadRecoveryHangfire(databaseConnectionString);
builder.Services.AddSmsProvider(new SmsProviderOptions(
    builder.Configuration["SMS_PROVIDER"] ?? "fake",
    builder.Configuration.GetValue("ALLOW_REAL_SMS", false),
    builder.Configuration["TWILIO_ACCOUNT_SID"],
    builder.Configuration["TWILIO_AUTH_TOKEN"]));
if (aiEnabled)
{
    string provider = builder.Configuration["AI_PROVIDER"] ??
        builder.Configuration["Ai:Provider"] ??
        "openai";
    if (!provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("AI_PROVIDER must be 'openai' when AI is enabled.");
    }

    builder.Services.AddOpenAiLeadAnalysis(new OpenAiLeadAnalysisOptions(
        builder.Configuration["OPENAI_API_KEY"] ?? string.Empty,
        builder.Configuration["AI_MODEL"] ??
            builder.Configuration["Ai:Model"] ??
            OpenAiLeadAnalysisOptions.DefaultModel,
        TimeSpan.FromSeconds(
            builder.Configuration.GetValue<int?>("AI_TIMEOUT_SECONDS") ??
            builder.Configuration.GetValue("Ai:TimeoutSeconds", 15)),
        builder.Configuration.GetValue<int?>("AI_MAX_RETRIES") ??
            builder.Configuration.GetValue("Ai:MaximumRetryCount", 2),
        TimeSpan.FromMilliseconds(
            builder.Configuration.GetValue<int?>("AI_RETRY_BASE_DELAY_MILLISECONDS") ??
            builder.Configuration.GetValue("Ai:RetryBaseDelayMilliseconds", 250)),
        builder.Configuration.GetValue<int?>("AI_MAX_OUTPUT_TOKENS") ??
            builder.Configuration.GetValue("Ai:MaximumOutputTokens", 1_000)));
}
builder.Services.AddSingleton(new SmsWorkerOptions(
    new Uri(baseUri, "/api/v1/webhooks/twilio/sms/status"),
    TimeSpan.FromSeconds(1),
    TimeSpan.FromMinutes(5)));
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["sms", "analysis"];
    options.WorkerCount = builder.Configuration.GetValue<int?>("JOBS_WORKER_COUNT") ??
        builder.Configuration.GetValue("SMS_WORKER_COUNT", 2);
});
builder.Services.AddHostedService<ScheduledActionDispatcher>();

var host = builder.Build();
await host.RunAsync();
