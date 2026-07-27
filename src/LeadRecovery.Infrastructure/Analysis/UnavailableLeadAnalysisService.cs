using LeadRecovery.Application.Analysis;

namespace LeadRecovery.Infrastructure.Analysis;

internal sealed class UnavailableLeadAnalysisService : ILeadAnalysisService
{
    public Task<LeadAnalysisResult> AnalyzeAsync(
        LeadAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LeadAnalysisResult.Failed(
            "Unavailable",
            "not-configured",
            1,
            new LeadAnalysisFailure(
                LeadAnalysisFailureKind.TransientProvider,
                "analysis_unavailable",
                IsRetryable: false)));
    }
}
