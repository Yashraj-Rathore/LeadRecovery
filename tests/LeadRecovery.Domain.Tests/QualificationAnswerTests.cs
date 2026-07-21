using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Domain.Tests;

public sealed class QualificationAnswerTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptedAnswerStoresNormalizedStructuredValue()
    {
        QualificationAnswer answer = Create(QualificationAnswerOutcome.Accepted, " Plumbing ");

        Assert.Equal("service", answer.QuestionKey);
        Assert.Equal("Plumbing", answer.Value);
        Assert.Equal(QualificationAnswerOutcome.Accepted, answer.Outcome);
    }

    [Theory]
    [InlineData(QualificationAnswerOutcome.Unknown)]
    [InlineData(QualificationAnswerOutcome.Ambiguous)]
    public void UnresolvedAnswerDoesNotPersistUntrustedValue(QualificationAnswerOutcome outcome)
    {
        QualificationAnswer answer = Create(outcome, "unclear customer response");

        Assert.Null(answer.Value);
        Assert.Equal(outcome, answer.Outcome);
    }

    [Fact]
    public void AcceptedAnswerRequiresAValue()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Create(QualificationAnswerOutcome.Accepted, null));
    }

    private static QualificationAnswer Create(
        QualificationAnswerOutcome outcome,
        string? value) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            " service ",
            value,
            outcome,
            CreatedAtUtc);
}
