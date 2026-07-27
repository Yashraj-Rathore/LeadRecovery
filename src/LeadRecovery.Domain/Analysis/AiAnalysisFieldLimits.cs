namespace LeadRecovery.Domain.Analysis;

public static class AiAnalysisFieldLimits
{
    public const int SchemaVersionMaximumLength = 20;
    public const int ProviderMaximumLength = 50;
    public const int ModelReferenceMaximumLength = 200;
    public const int InputHashLength = 64;
    public const int CategoryMaximumLength = 100;
    public const int SummaryMaximumLength = 1_000;
    public const int ExtractedValueMaximumLength = 200;
    public const int ReasonCodeMaximumLength = 64;
    public const int MaximumReasonCodes = 10;
    public const int SuggestedReplyMaximumLength = 1_000;
    public const int CorrectionReasonMaximumLength = 500;
    public const int JsonMaximumLength = 16_384;
}
