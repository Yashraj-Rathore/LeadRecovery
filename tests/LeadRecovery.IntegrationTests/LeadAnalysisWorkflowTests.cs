using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using LeadRecovery.Application.Analysis;
using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Analysis;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class LeadAnalysisWorkflowTests(LeadRecoveryApiFixture fixture)
{
    private const string AuthToken = "integration-test-twilio-auth-token";
    private static int phoneSequence;

    [Fact]
    public async Task ProviderOutagePreservesDeterministicResultRoutesHumanAndDoesNotRepeat()
    {
        using WebApplicationFactory<Program> application = CreateAiApplication();
        AnalysisSeed seed = await SeedAsync(application);
        using HttpResponseMessage response = await PostInboundAsync(application, seed);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Guid actionId;
        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            Lead deterministicLead = await dbContext.Leads.IgnoreQueryFilters().SingleAsync(
                candidate => candidate.Id == seed.LeadId,
                TestContext.Current.CancellationToken);
            Assert.Equal(LeadStatus.BookingOffered, deterministicLead.Status);
            Assert.Equal(
                QualificationAnswerOutcome.Accepted,
                (await dbContext.QualificationAnswers.IgnoreQueryFilters().SingleAsync(
                    candidate => candidate.LeadId == seed.LeadId,
                    TestContext.Current.CancellationToken)).Outcome);
            Assert.Contains(
                await dbContext.ScheduledActions.IgnoreQueryFilters()
                    .Where(candidate => candidate.LeadId == seed.LeadId)
                    .ToArrayAsync(TestContext.Current.CancellationToken),
                candidate =>
                    candidate.ActionType ==
                    WorkflowScheduledActionTypes.SendBookingLink);
            actionId = (await dbContext.ScheduledActions.IgnoreQueryFilters().SingleAsync(
                candidate =>
                    candidate.LeadId == seed.LeadId &&
                    candidate.ActionType == LeadAnalysisScheduledActionTypes.AnalyzeLead,
                TestContext.Current.CancellationToken)).Id;
        }

        LeadAnalysisWorkflowOutcome first;
        LeadAnalysisWorkflowOutcome duplicate;
        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            ExecuteScheduledLeadAnalysisUseCase useCase =
                scope.ServiceProvider.GetRequiredService<ExecuteScheduledLeadAnalysisUseCase>();
            first = await useCase.ExecuteAsync(
                actionId,
                seed.TenantId,
                "ai-provider-outage",
                TestContext.Current.CancellationToken);
            duplicate = await useCase.ExecuteAsync(
                actionId,
                seed.TenantId,
                "ai-provider-outage-duplicate",
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(LeadAnalysisWorkflowOutcome.FallbackNeedsHuman, first);
        Assert.Equal(LeadAnalysisWorkflowOutcome.Ignored, duplicate);
        await using AsyncServiceScope verificationScope =
            application.Services.CreateAsyncScope();
        LeadRecoveryDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Lead lead = await verificationContext.Leads.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.Id == seed.LeadId,
            TestContext.Current.CancellationToken);
        ScheduledAction analysisAction =
            await verificationContext.ScheduledActions.IgnoreQueryFilters().SingleAsync(
                candidate => candidate.Id == actionId,
                TestContext.Current.CancellationToken);
        Assert.Equal(LeadStatus.NeedsHuman, lead.Status);
        Assert.Equal(ScheduledActionStatus.Failed, analysisAction.Status);
        Assert.Equal(1, analysisAction.AttemptCount);
        Assert.Contains("analysis_unavailable", analysisAction.LastError);
        Assert.Empty(await verificationContext.AiAnalyses.IgnoreQueryFilters()
            .Where(candidate => candidate.LeadId == seed.LeadId)
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Single(await verificationContext.Messages.IgnoreQueryFilters()
            .Where(candidate => candidate.LeadId == seed.LeadId)
            .ToArrayAsync(TestContext.Current.CancellationToken));
        string failureAudit = (await verificationContext.AuditEvents
            .SingleAsync(
                candidate =>
                    candidate.TenantId == seed.TenantId &&
                    candidate.Action == "AiAnalysisFailed",
                TestContext.Current.CancellationToken)).AfterJson!;
        Assert.DoesNotContain("Plumbing", failureAudit, StringComparison.Ordinal);
        Assert.DoesNotContain(seed.CustomerPhone, failureAudit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidSuggestionIsPersistedOnceWithoutCustomerAction()
    {
        using WebApplicationFactory<Program> application = CreateAiApplication();
        AnalysisSeed seed = await SeedAsync(application);
        using HttpResponseMessage response = await PostInboundAsync(application, seed);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Guid actionId;
        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            actionId = (await dbContext.ScheduledActions.IgnoreQueryFilters().SingleAsync(
                candidate =>
                    candidate.LeadId == seed.LeadId &&
                    candidate.ActionType == LeadAnalysisScheduledActionTypes.AnalyzeLead,
                TestContext.Current.CancellationToken)).Id;
        }

        StubAnalysisService provider = new();
        LeadAnalysisWorkflowOutcome first;
        LeadAnalysisWorkflowOutcome duplicate;
        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            ExecuteScheduledLeadAnalysisUseCase useCase = new(
                scope.ServiceProvider.GetRequiredService<ILeadAnalysisWorkflowPersistence>(),
                provider,
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            first = await useCase.ExecuteAsync(
                actionId,
                seed.TenantId,
                "ai-success",
                TestContext.Current.CancellationToken);
            duplicate = await useCase.ExecuteAsync(
                actionId,
                seed.TenantId,
                "ai-success-duplicate",
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(LeadAnalysisWorkflowOutcome.Persisted, first);
        Assert.Equal(LeadAnalysisWorkflowOutcome.Ignored, duplicate);
        Assert.Equal(1, provider.InvocationCount);
        await using AsyncServiceScope verificationScope =
            application.Services.CreateAsyncScope();
        LeadRecoveryDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        AiAnalysis analysis =
            await verificationContext.AiAnalyses.IgnoreQueryFilters().SingleAsync(
                candidate => candidate.LeadId == seed.LeadId,
                TestContext.Current.CancellationToken);
        Lead lead = await verificationContext.Leads.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.Id == seed.LeadId,
            TestContext.Current.CancellationToken);
        Assert.Equal("Plumbing", analysis.CategorySuggestion);
        Assert.Equal(["Plumbing", "HVAC"], analysis.GetAllowedCategories());
        Assert.Equal(64, analysis.InputHash.Length);
        Assert.Equal(LeadStatus.BookingOffered, lead.Status);
        Assert.Equal(
            ScheduledActionStatus.Completed,
            (await verificationContext.ScheduledActions.IgnoreQueryFilters().SingleAsync(
                candidate => candidate.Id == actionId,
                TestContext.Current.CancellationToken)).Status);
        Assert.Single(await verificationContext.Messages.IgnoreQueryFilters()
            .Where(candidate => candidate.LeadId == seed.LeadId)
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewInboundReplyCoalescesPendingAnalysisWork()
    {
        using WebApplicationFactory<Program> application = CreateAiApplication();
        AnalysisSeed seed = await SeedAsync(application);
        using HttpResponseMessage firstResponse = await PostInboundAsync(application, seed);
        using HttpResponseMessage secondResponse = await PostInboundAsync(application, seed);

        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondResponse.StatusCode);

        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        ScheduledAction[] analysisActions = await dbContext.ScheduledActions
            .IgnoreQueryFilters()
            .Where(candidate =>
                candidate.LeadId == seed.LeadId &&
                candidate.ActionType == LeadAnalysisScheduledActionTypes.AnalyzeLead)
            .OrderBy(candidate => candidate.CreatedAtUtc)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, analysisActions.Length);
        Assert.Single(
            analysisActions,
            candidate => candidate.Status == ScheduledActionStatus.Cancelled);
        Assert.Single(
            analysisActions,
            candidate => candidate.Status == ScheduledActionStatus.Pending);
        Assert.All(analysisActions, candidate => Assert.Equal(0, candidate.AttemptCount));
    }

    private WebApplicationFactory<Program> CreateAiApplication() =>
        fixture.Application.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AI_ENABLED", "true");
            builder.UseSetting("AI_CATEGORY_QUESTION_KEY", "service");
        });

    private static async Task<AnalysisSeed> SeedAsync(
        WebApplicationFactory<Program> application)
    {
        int sequence = Interlocked.Increment(ref phoneSequence);
        string businessPhone = $"+1416558{sequence:D4}";
        string customerPhone = $"+1416559{sequence:D4}";
        Guid tenantId = Guid.CreateVersion7();
        Guid leadId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(-2);
        Tenant tenant = new(
            tenantId,
            "AI Workflow Test",
            $"ai-workflow-{tenantId:N}",
            "UTC",
            now);
        tenant.ChangeStatus(TenantStatus.Active, now);
        tenant.SetAutomationEnabled(true, now);
        TenantPhoneNumber number = new(
            Guid.CreateVersion7(),
            tenantId,
            "Twilio",
            businessPhone,
            $"PN{Guid.NewGuid():N}",
            ["no-answer"],
            0,
            300,
            isPrimary: true);
        Lead lead = new(
            leadId,
            tenantId,
            customerPhone,
            LeadSource.MissedCall,
            now);
        lead.BeginContacting(now);
        lead.AwaitCustomer(now);
        WorkflowDefinition workflow = new(
            Guid.CreateVersion7(),
            tenantId,
            "AI-enabled workflow",
            1,
            "https://booking.example.test/ai-workflow",
            [
                new QualificationQuestionPolicy(
                    "service",
                    "Which service do you need?",
                    QualificationAnswerKind.Choice,
                    ["Plumbing", "HVAC"]),
            ],
            new BusinessHoursPolicy(
                Enum.GetValues<DayOfWeek>()
                    .Select(day => new BusinessDayHours(
                        day,
                        new TimeOnly(0, 0),
                        new TimeOnly(23, 59)))
                    .ToArray(),
                true),
            [],
            now);
        workflow.Activate(now);
        ScheduledAction completedQuestion = new(
            Guid.CreateVersion7(),
            tenantId,
            leadId,
            WorkflowScheduledActionTypes.SendQualificationQuestion,
            now,
            $"ai-workflow-question:{leadId:N}",
            WorkflowScheduledActionPayloadSerializer.Serialize(new(
                1,
                "qualification:service",
                "service",
                null,
                null,
                null)),
            now);
        completedQuestion.Start(now);
        completedQuestion.Complete(now);

        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.Add(tenant);
        dbContext.TenantPhoneNumbers.Add(number);
        dbContext.Leads.Add(lead);
        dbContext.WorkflowDefinitions.Add(workflow);
        dbContext.ScheduledActions.Add(completedQuestion);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new AnalysisSeed(tenantId, leadId, businessPhone, customerPhone);
    }

    private static async Task<HttpResponseMessage> PostInboundAsync(
        WebApplicationFactory<Program> application,
        AnalysisSeed seed)
    {
        string path = "/api/v1/webhooks/twilio/sms/inbound";
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["MessageSid"] = $"SM{Guid.NewGuid():N}",
            ["From"] = seed.CustomerPhone,
            ["To"] = seed.BusinessPhone,
            ["Body"] = "Plumbing",
        };
        using HttpClient client = application.CreateClient();
        using FormUrlEncodedContent content = new(form);
        content.Headers.Add("X-Twilio-Signature", ComputeSignature(path, form));
        return await client.PostAsync(
            path,
            content,
            TestContext.Current.CancellationToken);
    }

    private static string ComputeSignature(
        string path,
        IReadOnlyDictionary<string, string> form)
    {
        StringBuilder signedValue = new($"https://webhooks.example.test{path}");
        foreach ((string key, string value) in
            form.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _ = signedValue.Append(key).Append(value);
        }

#pragma warning disable CA5350 // Twilio's signature protocol requires HMAC-SHA1.
        using HMACSHA1 hmac = new(Encoding.UTF8.GetBytes(AuthToken));
#pragma warning restore CA5350
        return Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(signedValue.ToString())));
    }

    private sealed record AnalysisSeed(
        Guid TenantId,
        Guid LeadId,
        string BusinessPhone,
        string CustomerPhone);

    private sealed class StubAnalysisService : ILeadAnalysisService
    {
        public int InvocationCount { get; private set; }

        public Task<LeadAnalysisResult> AnalyzeAsync(
            LeadAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(LeadAnalysisResult.Success(
                "Test",
                "deterministic-fixture",
                1,
                new LeadAnalysisSuggestion(
                    LeadAnalysisSchema.CurrentVersion,
                    "Plumbing",
                    LeadUrgency.Normal,
                    "Customer requests plumbing service.",
                    new LeadAnalysisExtractedFields(null, null, null),
                    0.94,
                    RequiresHumanReview: false,
                    [],
                    "A staff member can follow up.")));
        }
    }

    private sealed class TenantClaimScope : IDisposable
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly HttpContext? _previousContext;

        public TenantClaimScope(IServiceProvider services, Guid tenantId)
        {
            _accessor = services.GetRequiredService<IHttpContextAccessor>();
            _previousContext = _accessor.HttpContext;
            ClaimsIdentity identity = new(
                [new Claim(TenantClaimTypes.TenantId, tenantId.ToString())],
                "IntegrationTest");
            _accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            };
        }

        public void Dispose() => _accessor.HttpContext = _previousContext;
    }
}
