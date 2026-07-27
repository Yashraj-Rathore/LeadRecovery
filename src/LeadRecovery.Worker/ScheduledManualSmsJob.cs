using Hangfire;

using LeadRecovery.Application.Messaging;
using LeadRecovery.Infrastructure.Observability;

namespace LeadRecovery.Worker;

public sealed partial class ScheduledManualSmsJob(
    SendScheduledManualSmsUseCase useCase,
    SmsWorkerOptions options,
    ILogger<ScheduledManualSmsJob> logger,
    IHostEnvironment environment)
{
    private static readonly string ServiceVersion =
        typeof(ScheduledManualSmsJob).Assembly.GetName().Version?.ToString() ?? "unknown";

    [Queue("sms")]
    [AutomaticRetry(
        Attempts = 3,
        DelaysInSeconds = new[] { 30, 120, 300 },
        OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public Task ExecuteAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(
            actionId,
            tenantId,
            correlationId,
            traceParent: null,
            traceState: null,
            cancellationToken);

    [Queue("sms")]
    [AutomaticRetry(
        Attempts = 3,
        DelaysInSeconds = new[] { 30, 120, 300 },
        OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public Task ExecuteWithTelemetryAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        string? traceParent,
        string? traceState,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(
            actionId,
            tenantId,
            correlationId,
            traceParent,
            traceState,
            cancellationToken);

    private async Task ExecuteCoreAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        string? traceParent,
        string? traceState,
        CancellationToken cancellationToken)
    {
        using TelemetryOperation telemetry = LeadRecoveryTelemetry.StartJob(
            "manual_sms",
            tenantId,
            actionId,
            traceParent,
            traceState);
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "LeadRecovery.Worker",
            ["ServiceVersion"] = ServiceVersion,
            ["Environment"] = environment.EnvironmentName,
            ["JobType"] = "manual_sms",
            ["TenantId"] = tenantId,
            ["ScheduledActionId"] = actionId,
            ["CorrelationId"] = correlationId,
        });
        LogStarting(logger);
        try
        {
            OutboundSmsOutcome outcome = await useCase.ExecuteAsync(
                actionId,
                tenantId,
                correlationId,
                options.StatusCallbackUri,
                cancellationToken);
            telemetry.Complete(outcome.ToString());
            LogFinished(logger, outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            telemetry.Complete("Cancelled");
            throw;
        }
        catch
        {
            telemetry.Complete("UnhandledError", isError: true);
            throw;
        }
    }

    [LoggerMessage(
        EventId = 2020,
        Level = LogLevel.Information,
        Message = "Starting scheduled manual SMS action.")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(
        EventId = 2021,
        Level = LogLevel.Information,
        Message = "Finished scheduled manual SMS action with outcome {Outcome}.")]
    private static partial void LogFinished(ILogger logger, OutboundSmsOutcome outcome);
}
