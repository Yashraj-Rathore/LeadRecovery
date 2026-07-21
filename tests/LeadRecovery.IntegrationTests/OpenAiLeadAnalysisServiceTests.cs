using System.Net;
using System.Text.Json;

using LeadRecovery.Application.Analysis;
using LeadRecovery.Infrastructure.Analysis;

using Microsoft.Extensions.Logging.Abstractions;

namespace LeadRecovery.IntegrationTests;

public sealed class OpenAiLeadAnalysisServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("a3f17068-59b4-47b4-92d6-c50a2fe0a9c7");

    [Fact]
    public async Task SendsStrictMinimumRedactedInputAndReturnsSuggestion()
    {
        SequenceHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, CompletedResponse(ValidStructuredOutput));
        using HttpClient client = new(handler);
        OpenAiLeadAnalysisService service = CreateService(client, maximumRetryCount: 2);
        List<ConversationTurn> turns = Enumerable.Range(0, 10)
            .Select(index => new ConversationTurn(
                index % 2 == 0
                    ? ConversationParticipant.Customer
                    : ConversationParticipant.Business,
                $"turn-{index}"))
            .ToList();
        turns[^1] = new ConversationTurn(
            ConversationParticipant.Customer,
            "Call me at +1 (416) 555-0199 or caller@example.test about the leak.");
        LeadAnalysisRequest request = new(
            TenantId,
            ["Leak Repair", "Drain Cleaning"],
            turns,
            LeadAnalysisSchema.CurrentVersion,
            "Toronto; dispatcher@example.test; +1 905 555 0100");

        LeadAnalysisResult result = await service.AnalyzeAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal("Leak Repair", result.Suggestion?.ServiceCategory);
        Assert.Single(handler.Requests);
        CapturedRequest captured = handler.Requests[0];
        Assert.Equal("Bearer", captured.AuthorizationScheme);
        Assert.Equal("test-api-key", captured.AuthorizationParameter);
        Assert.DoesNotContain("caller@example.test", captured.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("416", captured.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("dispatcher@example.test", captured.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(TenantId.ToString(), captured.Body, StringComparison.OrdinalIgnoreCase);

        using JsonDocument payload = JsonDocument.Parse(captured.Body);
        JsonElement root = payload.RootElement;
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("gpt-5.6-sol", root.GetProperty("model").GetString());
        Assert.StartsWith("tenant_", root.GetProperty("safety_identifier").GetString());
        JsonElement format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.False(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());

        using JsonDocument providerInput = JsonDocument.Parse(root.GetProperty("input").GetString()!);
        JsonElement inputRoot = providerInput.RootElement;
        Assert.Equal(8, inputRoot.GetProperty("conversation").GetArrayLength());
        Assert.Equal("turn-2", inputRoot.GetProperty("conversation")[0].GetProperty("text").GetString());
        string lastTurn = inputRoot.GetProperty("conversation")[7].GetProperty("text").GetString()!;
        Assert.Contains("[phone]", lastTurn, StringComparison.Ordinal);
        Assert.Contains("[email]", lastTurn, StringComparison.Ordinal);
        string serviceArea = inputRoot.GetProperty("serviceAreaRules").GetString()!;
        Assert.Contains("[phone]", serviceArea, StringComparison.Ordinal);
        Assert.Contains("[email]", serviceArea, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetriesOnlyTransientResponsesWithinConfiguredBound()
    {
        SequenceHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.ServiceUnavailable, "{}");
        handler.EnqueueJson(HttpStatusCode.TooManyRequests, "{}");
        handler.EnqueueJson(HttpStatusCode.OK, CompletedResponse(ValidStructuredOutput));
        using HttpClient client = new(handler);
        OpenAiLeadAnalysisService service = CreateService(client, maximumRetryCount: 2);

        LeadAnalysisResult result = await service.AnalyzeAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task TimeoutReturnsFailureWithoutUnboundedRetries()
    {
        SequenceHandler handler = new();
        handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using HttpClient client = new(handler);
        OpenAiLeadAnalysisService service = CreateService(
            client,
            timeout: TimeSpan.FromSeconds(1),
            maximumRetryCount: 0);

        LeadAnalysisResult result = await service.AnalyzeAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(result.Suggestion);
        Assert.Equal(LeadAnalysisFailureKind.Timeout, result.Failure?.Kind);
        Assert.Equal(1, result.AttemptCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task InvalidStructuredOutputCreatesFailureNotSuggestion()
    {
        string invalidOutput = ValidStructuredOutput.Replace(
            "Leak Repair",
            "Unapproved Category",
            StringComparison.Ordinal);
        SequenceHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, CompletedResponse(invalidOutput));
        using HttpClient client = new(handler);
        OpenAiLeadAnalysisService service = CreateService(client, maximumRetryCount: 2);

        LeadAnalysisResult result = await service.AnalyzeAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(result.Suggestion);
        Assert.Equal(LeadAnalysisFailureKind.InvalidOutput, result.Failure?.Kind);
        Assert.False(result.Failure?.IsRetryable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RefusalCreatesFailureNotSuggestion()
    {
        SequenceHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, RefusalResponse());
        using HttpClient client = new(handler);
        OpenAiLeadAnalysisService service = CreateService(client, maximumRetryCount: 2);

        LeadAnalysisResult result = await service.AnalyzeAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(LeadAnalysisFailureKind.Refused, result.Failure?.Kind);
        Assert.Null(result.Suggestion);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void OptionsRejectUnboundedRetryCount(int maximumRetryCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAiLeadAnalysisOptions(
            "test-api-key",
            "gpt-5.6-sol",
            maximumRetryCount: maximumRetryCount));
    }

    private static OpenAiLeadAnalysisService CreateService(
        HttpClient client,
        TimeSpan? timeout = null,
        int maximumRetryCount = 0) =>
        new(
            client,
            new OpenAiLeadAnalysisOptions(
                "test-api-key",
                "gpt-5.6-sol",
                timeout,
                maximumRetryCount,
                TimeSpan.FromMilliseconds(50),
                endpoint: new Uri("https://api.openai.test/v1/responses")),
            new LeadAnalysisResultValidator(),
            NullLogger<OpenAiLeadAnalysisService>.Instance);

    private static LeadAnalysisRequest CreateRequest() =>
        new(
            TenantId,
            ["Leak Repair", "Drain Cleaning"],
            [new ConversationTurn(ConversationParticipant.Customer, "Basement leak")],
            LeadAnalysisSchema.CurrentVersion);

    private static string CompletedResponse(string structuredOutput) =>
        JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[]
            {
                new
                {
                    type = "message",
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text = structuredOutput,
                        },
                    },
                },
            },
        });

    private static string RefusalResponse() =>
        JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[]
            {
                new
                {
                    type = "message",
                    content = new[]
                    {
                        new
                        {
                            type = "refusal",
                            refusal = "Unable to comply.",
                        },
                    },
                },
            },
        });

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

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>
            responses = [];

        public List<CapturedRequest> Requests { get; } = [];

        public void EnqueueJson(HttpStatusCode statusCode, string json) =>
            Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json),
            }));

        public void Enqueue(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) =>
            responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No fake provider response was queued.");
            }

            return await responses.Dequeue()(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        string Body,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}
