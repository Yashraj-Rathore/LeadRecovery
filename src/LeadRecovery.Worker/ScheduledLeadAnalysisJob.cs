using Hangfire;

using LeadRecovery.Application.Analysis;

namespace LeadRecovery.Worker;

public sealed partial class ScheduledLeadAnalysisJob(
    ExecuteScheduledLeadAnalysisUseCase useCase,
    ILogger<ScheduledLeadAnalysisJob> logger)
{
    [Queue("analysis")]
    [AutomaticRetry(
        Attempts = 0,
        OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenantId,
            ["ScheduledActionId"] = actionId,
            ["CorrelationId"] = correlationId,
        });
        LogStarting(logger);
        LeadAnalysisWorkflowOutcome outcome = await useCase.ExecuteAsync(
            actionId,
            tenantId,
            correlationId,
            cancellationToken);
        LogFinished(logger, outcome);
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
