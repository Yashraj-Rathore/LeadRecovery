using Hangfire;

using LeadRecovery.Application.Messaging;

namespace LeadRecovery.Worker;

public sealed partial class ScheduledRecoverySmsJob(
    SendScheduledRecoverySmsUseCase useCase,
    SmsWorkerOptions options,
    ILogger<ScheduledRecoverySmsJob> logger)
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
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Starting scheduled recovery SMS action.")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Finished scheduled recovery SMS action with outcome {Outcome}.")]
    private static partial void LogFinished(ILogger logger, OutboundSmsOutcome outcome);
}
