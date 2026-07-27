namespace LeadRecovery.Application.Analysis;

public sealed class ExecuteScheduledLeadAnalysisUseCase(
    ILeadAnalysisWorkflowPersistence persistence,
    ILeadAnalysisService analysisService,
    TimeProvider timeProvider)
{
    public async Task<LeadAnalysisWorkflowOutcome> ExecuteAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        PreparedLeadAnalysis? prepared = await persistence.PrepareAsync(
            actionId,
            tenantId,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (prepared is null)
        {
            return LeadAnalysisWorkflowOutcome.Ignored;
        }

        LeadAnalysisResult result = await analysisService.AnalyzeAsync(
            prepared.Request,
            cancellationToken);
        return await persistence.CompleteAsync(
            prepared,
            result,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
