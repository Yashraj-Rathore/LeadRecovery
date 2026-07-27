using LeadRecovery.Application.Analysis;

namespace LeadRecovery.Application.Tests;

public sealed class LeadAnalysisWorkflowTests
{
    private static readonly Guid TenantId =
        Guid.Parse("bd6b483d-cc6d-48ba-a8d0-01ce91890cef");

    [Fact]
    public void InputHashIsDeterministicAndChangesWithRelevantInput()
    {
        LeadAnalysisInputHasher hasher = new();
        LeadAnalysisRequest first = CreateRequest("Basement leak");
        LeadAnalysisRequest equivalent = CreateRequest("Basement leak");
        LeadAnalysisRequest changed = CreateRequest("Blocked drain");

        string firstHash = hasher.ComputeHash(first);

        Assert.Equal(64, firstHash.Length);
        Assert.Equal(firstHash, hasher.ComputeHash(equivalent));
        Assert.NotEqual(firstHash, hasher.ComputeHash(changed));
    }

    [Fact]
    public void ScheduledPayloadRejectsAdditionalOrInvalidProperties()
    {
        LeadAnalysisScheduledActionPayload payload = new(
            1,
            LeadAnalysisSchema.CurrentVersion,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            2,
            "service",
            ["Plumbing", "HVAC"]);
        string validJson = LeadAnalysisScheduledActionPayloadSerializer.Serialize(payload);
        string extraProperty = validJson.Insert(validJson.Length - 1, ""","extra":true""");

        Assert.True(LeadAnalysisScheduledActionPayloadSerializer.TryDeserialize(
            validJson,
            out LeadAnalysisScheduledActionPayload? parsed));
        Assert.Equal(payload.SourceMessageId, parsed?.SourceMessageId);
        Assert.False(LeadAnalysisScheduledActionPayloadSerializer.TryDeserialize(
            extraProperty,
            out _));
        Assert.False(LeadAnalysisScheduledActionPayloadSerializer.TryDeserialize(
            """{"schemaVersion":1}""",
            out _));
    }

    [Fact]
    public async Task IgnoredPreparationDoesNotInvokeProvider()
    {
        StubPersistence persistence = new(null);
        StubAnalysisService provider = new(CreateFailure());
        ExecuteScheduledLeadAnalysisUseCase useCase = new(
            persistence,
            provider,
            TimeProvider.System);

        LeadAnalysisWorkflowOutcome outcome = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            TenantId,
            "analysis-ignored",
            TestContext.Current.CancellationToken);

        Assert.Equal(LeadAnalysisWorkflowOutcome.Ignored, outcome);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(persistence.CompletedResult);
    }

    [Fact]
    public async Task TypedProviderFailureIsCompletedWithoutApplicationRetry()
    {
        LeadAnalysisRequest request = CreateRequest("Provider outage test");
        PreparedLeadAnalysis prepared = new(
            TenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new LeadAnalysisInputHasher().ComputeHash(request),
            request);
        StubPersistence persistence = new(prepared)
        {
            CompletionOutcome = LeadAnalysisWorkflowOutcome.FallbackNeedsHuman,
        };
        StubAnalysisService provider = new(CreateFailure());
        ExecuteScheduledLeadAnalysisUseCase useCase = new(
            persistence,
            provider,
            TimeProvider.System);

        LeadAnalysisWorkflowOutcome outcome = await useCase.ExecuteAsync(
            prepared.ActionId,
            TenantId,
            "analysis-outage",
            TestContext.Current.CancellationToken);

        Assert.Equal(LeadAnalysisWorkflowOutcome.FallbackNeedsHuman, outcome);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Same(provider.Result, persistence.CompletedResult);
    }

    private static LeadAnalysisRequest CreateRequest(string message) =>
        new(
            TenantId,
            ["Plumbing", "HVAC"],
            [new ConversationTurn(ConversationParticipant.Customer, message)],
            LeadAnalysisSchema.CurrentVersion);

    private static LeadAnalysisResult CreateFailure() =>
        LeadAnalysisResult.Failed(
            "Test",
            "unavailable",
            3,
            new LeadAnalysisFailure(
                LeadAnalysisFailureKind.TransientProvider,
                "provider_unavailable",
                IsRetryable: true));

    private sealed class StubAnalysisService(LeadAnalysisResult result)
        : ILeadAnalysisService
    {
        public int InvocationCount { get; private set; }

        public LeadAnalysisResult Result { get; } = result;

        public Task<LeadAnalysisResult> AnalyzeAsync(
            LeadAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubPersistence(PreparedLeadAnalysis? prepared)
        : ILeadAnalysisWorkflowPersistence
    {
        public LeadAnalysisResult? CompletedResult { get; private set; }

        public LeadAnalysisWorkflowOutcome CompletionOutcome { get; init; } =
            LeadAnalysisWorkflowOutcome.Persisted;

        public Task<PreparedLeadAnalysis?> PrepareAsync(
            Guid actionId,
            Guid tenantId,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(prepared);

        public Task<LeadAnalysisWorkflowOutcome> CompleteAsync(
            PreparedLeadAnalysis preparedAnalysis,
            LeadAnalysisResult result,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CompletedResult = result;
            return Task.FromResult(CompletionOutcome);
        }
    }
}
