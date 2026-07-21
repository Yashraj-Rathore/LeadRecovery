using LeadRecovery.Application.Analysis;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Tests;

public sealed class LeadAnalysisTests
{
    private static readonly Guid TenantId = Guid.Parse("f0f6683b-561d-4ff5-a442-daef8736b21f");

    private readonly LeadAnalysisResultValidator validator = new();

    [Fact]
    public void RequestCopiesAndNormalizesBoundedInput()
    {
        string[] categories = [" Leak Repair ", "Drain Cleaning"];
        ConversationTurn[] turns =
        [
            new(ConversationParticipant.Customer, "  Water is leaking in the basement.  "),
        ];

        LeadAnalysisRequest request = new(
            TenantId,
            categories,
            turns,
            LeadAnalysisSchema.CurrentVersion,
            "  Ontario pilot service area  ");
        categories[0] = "Changed";

        Assert.Equal(["Leak Repair", "Drain Cleaning"], request.AllowedCategories);
        Assert.Equal("Water is leaking in the basement.", request.Turns[0].Text);
        Assert.Equal("Ontario pilot service area", request.ServiceAreaRules);
    }

    [Theory]
    [InlineData("Unknown", "is reserved")]
    [InlineData("leak repair", "must be unique")]
    public void RequestRejectsReservedOrDuplicateCategories(
        string secondCategory,
        string expectedMessage)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new LeadAnalysisRequest(
            TenantId,
            ["Leak Repair", secondCategory],
            [new ConversationTurn(ConversationParticipant.Customer, "Help")],
            LeadAnalysisSchema.CurrentVersion));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StrictValidatorReturnsTypedSuggestion()
    {
        LeadAnalysisValidationResult result = validator.Validate(
            ValidStructuredOutput,
            CreateRequest());

        Assert.True(result.IsValid);
        Assert.Equal("Leak Repair", result.Suggestion?.ServiceCategory);
        Assert.Equal(LeadUrgency.High, result.Suggestion?.Urgency);
        Assert.Equal(0.91, result.Suggestion?.Confidence);
        Assert.False(result.Suggestion?.RequiresHumanReview);
    }

    [Theory]
    [InlineData(0.84, "[]")]
    [InlineData(0.99, """["ACTIVE_PROPERTY_DAMAGE"]""")]
    public void ValidatorConservativelyRequiresReview(
        double confidence,
        string reasonCodesJson)
    {
        string output = ValidStructuredOutput
            .Replace("0.91", confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("[]", reasonCodesJson, StringComparison.Ordinal);

        LeadAnalysisValidationResult result = validator.Validate(output, CreateRequest());

        Assert.True(result.IsValid);
        Assert.True(result.Suggestion?.RequiresHumanReview);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"schemaVersion":"1.0"}""")]
    public void InvalidShapeIsNeverReturnedAsSuggestion(string output)
    {
        LeadAnalysisValidationResult result = validator.Validate(output, CreateRequest());

        Assert.False(result.IsValid);
        Assert.Null(result.Suggestion);
        Assert.NotNull(result.FailureCode);
    }

    [Fact]
    public void ExtraPropertyAndUnapprovedCategoryAreRejected()
    {
        string extraProperty = string.Concat(
            ",",
            '"',
            "untrusted",
            '"',
            ":true");
        string withExtraProperty = ValidStructuredOutput.Insert(
            ValidStructuredOutput.LastIndexOf('}'),
            extraProperty);
        string withUnapprovedCategory = ValidStructuredOutput.Replace(
            "Leak Repair",
            "Electrical",
            StringComparison.Ordinal);

        Assert.False(validator.Validate(withExtraProperty, CreateRequest()).IsValid);
        Assert.False(validator.Validate(withUnapprovedCategory, CreateRequest()).IsValid);
    }

    private static LeadAnalysisRequest CreateRequest() =>
        new(
            TenantId,
            ["Leak Repair", "Drain Cleaning"],
            [new ConversationTurn(ConversationParticipant.Customer, "Basement leak")],
            LeadAnalysisSchema.CurrentVersion);

    private const string ValidStructuredOutput = """
        {
          "schemaVersion": "1.0",
          "serviceCategory": "Leak Repair",
          "urgency": "High",
          "summary": "Customer reports a basement leak.",
          "extracted": {
            "city": "Mississauga",
            "postalCode": null,
            "preferredCallbackWindow": null
          },
          "confidence": 0.91,
          "requiresHumanReview": false,
          "reasonCodes": [],
          "suggestedReply": null
        }
        """;
}
