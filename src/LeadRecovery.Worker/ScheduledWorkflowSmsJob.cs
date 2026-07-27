using Hangfire;

using LeadRecovery.Application.Messaging;

namespace LeadRecovery.Worker;

public sealed partial class ScheduledWorkflowSmsJob(
    SendScheduledWorkflowSmsUseCase useCase,
    SmsWorkerOptions options,
    ILogger<ScheduledWorkflowSmsJob> logger)
{
    [Queue("sms")]
    [AutomaticRetry(
        Attempts = 3,
        DelaysInSeconds = new[] { 30, 120, 300 },
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
        OutboundSmsOutcome outcome = await useCase.ExecuteAsync(
            actionId,
            tenantId,
            correlationId,
            options.StatusCallbackUri,
            cancellationToken);
        LogFinished(logger, outcome);
    }

    [LoggerMessage(
        EventId = 2030,
        Level = LogLevel.Information,
        Message = "Starting scheduled workflow SMS action.")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(
        EventId = 2031,
        Level = LogLevel.Information,
        Message = "Finished scheduled workflow SMS action with outcome {Outcome}.")]
    private static partial void LogFinished(ILogger logger, OutboundSmsOutcome outcome);
}
