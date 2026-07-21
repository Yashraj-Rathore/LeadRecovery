using LeadRecovery.Domain.Automations;

namespace LeadRecovery.Domain.Tests;

public sealed class WorkflowDefinitionTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorPersistsValidatedVersionedPolicies()
    {
        WorkflowDefinition workflow = CreateWorkflow();

        workflow.Activate(CreatedAtUtc.AddMinutes(1));

        Assert.True(workflow.IsActive);
        Assert.Equal(1, workflow.Version);
        Assert.Equal("https://booking.example.test/acme", workflow.BookingUrl);
        QualificationQuestionPolicy question = Assert.Single(
            workflow.GetQualificationQuestions());
        Assert.Equal("service", question.Key);
        Assert.Equal(["Plumbing", "HVAC"], question.AllowedValues);
        Assert.Equal(5, workflow.GetBusinessHoursPolicy().Windows.Length);
        Assert.Equal(2, workflow.GetFollowUpSteps().Length);
    }

    [Theory]
    [InlineData("http://booking.example.test/acme")]
    [InlineData("/acme")]
    [InlineData("https://user:secret@booking.example.test/acme")]
    public void ConstructorRejectsUnapprovedBookingUrlShapes(string bookingUrl)
    {
        Assert.Throws<ArgumentException>(() => CreateWorkflow(bookingUrl));
    }

    [Fact]
    public void ConstructorRejectsMoreThanMaximumFollowUps()
    {
        FollowUpStepPolicy[] steps = Enumerable.Range(
            1,
            WorkflowDefinitionFieldLimits.MaximumFollowUps + 1)
            .Select(sequence => new FollowUpStepPolicy(
                sequence,
                sequence * 10,
                $"FollowUp{sequence}"))
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateWorkflow(followUps: steps));
    }

    [Fact]
    public void ConstructorRejectsDuplicateQuestionKeysAndInvalidWindows()
    {
        QualificationQuestionPolicy[] duplicateQuestions =
        [
            new("service", "What service?", QualificationAnswerKind.RequiredText, []),
            new("SERVICE", "Which service?", QualificationAnswerKind.RequiredText, []),
        ];
        Assert.Throws<ArgumentException>(() => CreateWorkflow(questions: duplicateQuestions));

        BusinessHoursPolicy invalidHours = new(
            [new BusinessDayHours(DayOfWeek.Monday, new TimeOnly(17, 0), new TimeOnly(9, 0))],
            false);
        Assert.Throws<ArgumentException>(() => CreateWorkflow(businessHours: invalidHours));
    }

    private static WorkflowDefinition CreateWorkflow(
        string bookingUrl = "https://booking.example.test/acme",
        QualificationQuestionPolicy[]? questions = null,
        BusinessHoursPolicy? businessHours = null,
        FollowUpStepPolicy[]? followUps = null) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Default recovery workflow",
            1,
            bookingUrl,
            questions ??
            [
                new(
                    "service",
                    "What service do you need?",
                    QualificationAnswerKind.Choice,
                    ["Plumbing", "HVAC"]),
            ],
            businessHours ?? new BusinessHoursPolicy(
                Enumerable.Range(1, 5)
                    .Select(day => new BusinessDayHours(
                        (DayOfWeek)day,
                        new TimeOnly(9, 0),
                        new TimeOnly(17, 0)))
                    .ToArray(),
                true),
            followUps ??
            [
                new(1, 30, "FollowUpOne"),
                new(2, 120, "FollowUpTwo"),
            ],
            CreatedAtUtc);
}
