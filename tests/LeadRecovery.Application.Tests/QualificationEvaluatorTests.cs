using LeadRecovery.Application.Automations;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Tests;

public sealed class QualificationEvaluatorTests
{
    private readonly QualificationEvaluator evaluator = new();

    [Fact]
    public void RequiredTextReturnsNormalizedAcceptedValue()
    {
        QualificationEvaluation result = evaluator.Evaluate(
            new QualificationQuestionPolicy(
                "description",
                "Describe the problem",
                QualificationAnswerKind.RequiredText,
                []),
            "  leaking kitchen pipe  ");

        Assert.Equal(QualificationAnswerOutcome.Accepted, result.Outcome);
        Assert.Equal("leaking kitchen pipe", result.Value);
    }

    [Theory]
    [InlineData("plumbing", QualificationAnswerOutcome.Accepted, "Plumbing")]
    [InlineData("I need HVAC service", QualificationAnswerOutcome.Accepted, "HVAC")]
    [InlineData("plumbing or HVAC", QualificationAnswerOutcome.Ambiguous, null)]
    [InlineData("something else", QualificationAnswerOutcome.Unknown, null)]
    public void ChoiceEvaluationIsDeterministic(
        string response,
        QualificationAnswerOutcome expectedOutcome,
        string? expectedValue)
    {
        QualificationQuestionPolicy question = new(
            "service",
            "Which service?",
            QualificationAnswerKind.Choice,
            ["Plumbing", "HVAC"]);

        QualificationEvaluation result = evaluator.Evaluate(question, response);

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(expectedValue, result.Value);
    }
}
