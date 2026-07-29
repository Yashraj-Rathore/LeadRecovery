using LeadRecovery.Application.Onboarding;

namespace LeadRecovery.Application.Tests;

public sealed class TenantOnboardingUseCaseTests
{
    [Fact]
    public void CompletePlanValidatesWithoutReadingSecrets()
    {
        RecordingStore store = new();
        RecordingSecrets secrets = new();

        TenantOnboardingValidationResult result =
            TenantOnboardingUseCase.Validate(CreatePlan());

        Assert.True(result.IsValid);
        Assert.NotNull(result.Plan);
        Assert.Empty(secrets.Names);
        Assert.Null(store.Plan);
    }

    [Fact]
    public void IncompletePlanCannotBeActivated()
    {
        TenantOnboardingPlan plan = CreatePlan() with
        {
            Templates = [],
            Users =
            [
                new("first@example.test", "First", "Staff", "FIRST_PASSWORD"),
                new("second@example.test", "Second", "Staff", "SECOND_PASSWORD"),
            ],
        };

        TenantOnboardingValidationResult result = TenantOnboardingUseCase.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Field == "templates");
        Assert.Contains(result.Errors, error => error.Field == "users" && error.Message.Contains("Owner", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidNestedPoliciesReturnFieldErrorsInsteadOfThrowing()
    {
        TenantOnboardingPlan plan = CreatePlan() with
        {
            Workflow = CreatePlan().Workflow with
            {
                QualificationQuestions = [new("", "", "RequiredText", [])],
                BusinessHours = [new("Monday", "18:00", "08:00")],
                FollowUps = [new(0, -1, "")],
            },
        };

        TenantOnboardingValidationResult result = TenantOnboardingUseCase.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Field.StartsWith("workflow", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteResolvesPasswordsFromEnvironmentAbstractionAndDelegatesAtomically()
    {
        RecordingStore store = new();
        RecordingSecrets secrets = new() { Values = { ["OWNER_PASSWORD"] = "Strong!Password123" } };
        TenantOnboardingUseCase useCase = new(store, secrets);

        TenantOnboardingResult result = await useCase.ExecuteAsync(
            CreatePlan(),
            TestContext.Current.CancellationToken);

        Assert.Equal(TenantOnboardingStatus.Activated, result.Status);
        Assert.NotNull(store.Plan);
        Assert.Equal("Strong!Password123", store.Passwords!["owner@example.test"]);
        Assert.Equal(["OWNER_PASSWORD"], secrets.Names);
    }

    private static TenantOnboardingPlan CreatePlan() => new(
        1,
        new("Northstar Home Services", "northstar-home-services", "America/Toronto"),
        new("Twilio", "+14165550100", "PN123", ["busy", "failed", "no-answer"], 60, 3600),
        new(
            "Default workflow",
            "https://booking.example.test/northstar",
            [new("problem", "Briefly describe the problem.", "RequiredText", [])],
            [new("Monday", "08:00", "18:00")],
            [new(1, 60, "WorkflowFollowUpOne")]),
        [
            new("Recovery", "InitialMissedCallRecovery", "Sorry we missed your call. Reply STOP to opt out."),
            new("Booking", "BookingLink", "Book here: {{BookingUrl}}"),
            new("Follow-up", "WorkflowFollowUpOne", "Are you still looking for help?"),
        ],
        [new("owner@example.test", "Owner", "Owner", "OWNER_PASSWORD")]);

    private sealed class RecordingStore : ITenantOnboardingStore
    {
        public ValidatedTenantOnboardingPlan? Plan { get; private set; }
        public IReadOnlyDictionary<string, string>? Passwords { get; private set; }

        public Task<TenantOnboardingResult> ProvisionAsync(
            ValidatedTenantOnboardingPlan plan,
            IReadOnlyDictionary<string, string> userPasswords,
            CancellationToken cancellationToken)
        {
            Plan = plan;
            Passwords = userPasswords;
            return Task.FromResult(TenantOnboardingResult.Activated(Guid.CreateVersion7()));
        }
    }

    private sealed class RecordingSecrets : IOnboardingSecretSource
    {
        public Dictionary<string, string> Values { get; } = [];
        public List<string> Names { get; } = [];

        public string? GetSecret(string name)
        {
            Names.Add(name);
            return Values.GetValueOrDefault(name);
        }
    }
}
