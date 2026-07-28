using Hangfire;

using LeadRecovery.Application.Retention;

namespace LeadRecovery.Worker;

public sealed partial class TenantRetentionJob(
    RetentionUseCase useCase,
    ILogger<TenantRetentionJob> logger)
{
    [Queue("maintenance")]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantRetentionResult> results =
            await useCase.ExecuteAsync(cancellationToken);
        foreach (TenantRetentionResult result in results)
        {
            LogTenantProcessed(
                logger,
                result.TenantId,
                result.Mode,
                result.CandidateLeadCount,
                result.DeletedLeadCount,
                result.AuditEventId);
        }
    }

    [LoggerMessage(
        EventId = 2020,
        Level = LogLevel.Information,
        Message = "Retention processed tenant {TenantId} in {Mode} mode: " +
            "{CandidateLeadCount} candidate Leads, {DeletedLeadCount} deleted; " +
            "audit {AuditEventId}.")]
    private static partial void LogTenantProcessed(
        ILogger logger,
        Guid tenantId,
        RetentionExecutionMode mode,
        int candidateLeadCount,
        int deletedLeadCount,
        Guid auditEventId);
}
