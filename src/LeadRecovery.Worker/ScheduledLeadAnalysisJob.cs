using Hangfire;

using LeadRecovery.Application.Analysis;
using LeadRecovery.Infrastructure.Observability;

namespace LeadRecovery.Worker;

public sealed partial class ScheduledLeadAnalysisJob(
    ExecuteScheduledLeadAnalysisUseCase useCase,
    ILogger<ScheduledLeadAnalysisJob> logger,
    IHostEnvironment environment)
{
    private static readonly string ServiceVersion =
        typeof(ScheduledLeadAnalysisJob).Assembly.GetName().Version?.ToString() ?? "unknown";

    [Queue("analysis")]
    [AutomaticRetry(
        Attempts = 0,
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

    [Queue("analysis")]
    [AutomaticRetry(
        Attempts = 0,
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
            "lead_analysis",
            tenantId,
            actionId,
            traceParent,
            traceState);
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "LeadRecovery.Worker",
            ["ServiceVersion"] = ServiceVersion,
            ["Environment"] = environment.EnvironmentName,
            ["JobType"] = "lead_analysis",
            ["TenantId"] = tenantId,
            ["ScheduledActionId"] = actionId,
            ["CorrelationId"] = correlationId,
        });
        LogStarting(logger);
        try
        {
            LeadAnalysisWorkflowOutcome outcome = await useCase.ExecuteAsync(
                actionId,
                tenantId,
                correlationId,
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
        EventId = 7_020,
        Level = LogLevel.Information,
        Message = "Starting scheduled lead analysis.")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(
        EventId = 7_021,
        Level = LogLevel.Information,
        Message = "Finished scheduled lead analysis with outcome {Outcome}.")]
    private static partial void LogFinished(
        ILogger logger,
        LeadAnalysisWorkflowOutcome outcome);
}
