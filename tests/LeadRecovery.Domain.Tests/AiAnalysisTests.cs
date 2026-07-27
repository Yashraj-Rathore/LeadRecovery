using LeadRecovery.Domain.Analysis;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Domain.Tests;

public sealed class AiAnalysisTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 27, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructionPreservesImmutableSuggestionAndReviewPolicy()
    {
        AiAnalysis analysis = CreateAnalysis();

        Assert.Equal(AiAnalysisReviewStatus.Pending, analysis.ReviewStatus);
        Assert.Equal(["LeakRepair", "DrainCleaning"], analysis.GetAllowedCategories());
        Assert.Equal(["ACTIVE_PROPERTY_DAMAGE"], analysis.GetReasonCodes());
        Assert.Equal("LeakRepair", analysis.GetSuggestion().ServiceCategory);
        Assert.Null(analysis.GetReviewedValues());
        Assert.Contains(
            nameof(AiAnalysis.SchemaVersion),
            analysis.RawStructuredOutputJson,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptCopiesSuggestionWithoutChangingOriginal()
    {
        AiAnalysis analysis = CreateAnalysis();
        Guid reviewerId = Guid.CreateVersion7();

        analysis.Accept(reviewerId, null, CreatedAtUtc.AddMinutes(1));

        Assert.Equal(AiAnalysisReviewStatus.Accepted, analysis.ReviewStatus);
        Assert.Equal(analysis.GetSuggestion(), analysis.GetReviewedValues());
        Assert.Equal(reviewerId, analysis.ReviewedByUserId);
        Assert.Throws<InvalidOperationException>(() =>
            analysis.Reject(reviewerId, null, CreatedAtUtc.AddMinutes(2)));
    }

    [Fact]
    public void EditValidatesCategoryAndStoresCorrectionSeparately()
    {
        AiAnalysis analysis = CreateAnalysis();
        AiAnalysisValues corrected = new(
            "DrainCleaning",
            LeadUrgency.Normal,
            "Customer clarified that the issue is a slow drain.",
            "Toronto",
            "M5V",
            "Tomorrow morning",
            "Thanks. A team member will review your request.");

        analysis.Edit(
            Guid.CreateVersion7(),
            corrected,
            "Customer clarified the service.",
            CreatedAtUtc.AddMinutes(2));

        Assert.Equal(AiAnalysisReviewStatus.Edited, analysis.ReviewStatus);
        Assert.Equal("LeakRepair", analysis.GetSuggestion().ServiceCategory);
        Assert.Equal(corrected, analysis.GetReviewedValues());
        Assert.Equal("Customer clarified the service.", analysis.CorrectionReason);
    }

    [Fact]
    public void RejectStoresDecisionWithoutCreatingReviewedValues()
    {
        AiAnalysis analysis = CreateAnalysis();

        analysis.Reject(
            Guid.CreateVersion7(),
            "Not enough information.",
            CreatedAtUtc.AddMinutes(1));

        Assert.Equal(AiAnalysisReviewStatus.Rejected, analysis.ReviewStatus);
        Assert.Null(analysis.GetReviewedValues());
        Assert.Equal("Not enough information.", analysis.CorrectionReason);
    }

    [Fact]
    public void InvalidInputHashCategoryAndConfidenceAreRejected()
    {
        AiAnalysisValues suggestion = CreateSuggestion();

        Assert.Throws<ArgumentException>(() => CreateAnalysis(inputHash: "not-a-hash"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAnalysis(confidence: 1.1));
        Assert.Throws<ArgumentException>(() =>
            new AiAnalysis(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "1.0",
                "Test",
                "fixture",
                new string('b', 64),
                ["DrainCleaning"],
                suggestion,
                0.5,
                true,
                [],
                CreatedAtUtc));
    }

    private static AiAnalysis CreateAnalysis(
        string? inputHash = null,
        double confidence = 0.58) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "1.0",
            "Test",
            "fictional-fixture",
            inputHash ?? new string('a', 64),
            ["LeakRepair", "DrainCleaning"],
            CreateSuggestion(),
            confidence,
            true,
            ["ACTIVE_PROPERTY_DAMAGE"],
            CreatedAtUtc);

    private static AiAnalysisValues CreateSuggestion() =>
        new(
            "LeakRepair",
            LeadUrgency.High,
            "Customer reports an active leak and requests a callback.",
            "Toronto",
            null,
            "As soon as possible",
            "Thanks. A team member will review this and contact you shortly.");
}
