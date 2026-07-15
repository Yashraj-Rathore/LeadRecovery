extern alias WorkerAssembly;

using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Hangfire;

using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure;
using LeadRecovery.Infrastructure.BackgroundJobs;
using LeadRecovery.Infrastructure.Messaging;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LeadRecovery.IntegrationTests;

using ScheduledRecoverySmsJob =
    WorkerAssembly::LeadRecovery.Worker.ScheduledRecoverySmsJob;
using SmsWorkerOptions = WorkerAssembly::LeadRecovery.Worker.SmsWorkerOptions;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class TwilioSmsWorkflowTests(LeadRecoveryApiFixture fixture)
{
    private const string AuthToken = "integration-test-twilio-auth-token";

    [Fact]
    public async Task DueActionUsesApprovedTemplateAndDuplicateExecutionDoesNotResend()
    {
        WorkflowSeed seed = await SeedWorkflowAsync("+14165550200", "+14165550201");
        OutboundSmsOutcome first;
        OutboundSmsOutcome duplicate;
        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            SendScheduledRecoverySmsUseCase useCase =
                scope.ServiceProvider.GetRequiredService<SendScheduledRecoverySmsUseCase>();
            first = await useCase.ExecuteAsync(
                seed.ActionId,
                seed.TenantId,
                "integration-outbound",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
            duplicate = await useCase.ExecuteAsync(
                seed.ActionId,
                seed.TenantId,
                "integration-duplicate",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(OutboundSmsOutcome.Accepted, first);
        Assert.Equal(OutboundSmsOutcome.Ignored, duplicate);
        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Message message = await dbContext.Messages.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.TenantId == seed.TenantId,
            TestContext.Current.CancellationToken);
        ScheduledAction action = await dbContext.ScheduledActions.IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.Id == seed.ActionId,
                TestContext.Current.CancellationToken);
        Lead lead = await dbContext.Leads.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.Id == seed.LeadId,
            TestContext.Current.CancellationToken);
        Assert.Equal(MessageStatus.Sent, message.Status);
        Assert.Equal("Alpha Test received your call. How can we help?", message.Body);
        Assert.Equal(seed.TemplateId, message.TemplateId);
        Assert.StartsWith("SM", message.ProviderMessageSid, StringComparison.Ordinal);
        Assert.Equal(ScheduledActionStatus.Completed, action.Status);
        Assert.Equal(LeadStatus.Contacting, lead.Status);
    }

    [Fact]
    public async Task SignedStopIsIdempotentCancelsPendingActionAndBlocksFutureSend()
    {
        WorkflowSeed seed = await SeedWorkflowAsync("+14165550210", "+14165550211");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["MessageSid"] = $"SM{Guid.NewGuid():N}",
            ["From"] = seed.CustomerPhone,
            ["To"] = seed.BusinessPhone,
            ["Body"] = " STOP ",
        };

        using HttpResponseMessage first = await PostSignedAsync(
            "/api/v1/webhooks/twilio/sms/inbound",
            form);
        using HttpResponseMessage duplicate = await PostSignedAsync(
            "/api/v1/webhooks/twilio/sms/inbound",
            form);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Customer customer = await dbContext.Customers.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.TenantId == seed.TenantId,
            TestContext.Current.CancellationToken);
        Lead lead = await dbContext.Leads.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.Id == seed.LeadId,
            TestContext.Current.CancellationToken);
        ScheduledAction action = await dbContext.ScheduledActions.IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.Id == seed.ActionId,
                TestContext.Current.CancellationToken);
        Assert.NotNull(customer.OptedOutAtUtc);
        Assert.Equal(AutomationState.SuppressedOptOut, lead.AutomationState);
        Assert.Equal(ScheduledActionStatus.Cancelled, action.Status);
        Assert.Equal(
            1,
            await dbContext.Messages.IgnoreQueryFilters().CountAsync(
                candidate => candidate.TenantId == seed.TenantId,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await dbContext.ExternalEventReceipts.CountAsync(
                candidate => candidate.TenantId == seed.TenantId &&
                    candidate.EventType == "InboundSms",
                TestContext.Current.CancellationToken));

        SendScheduledRecoverySmsUseCase useCase =
            scope.ServiceProvider.GetRequiredService<SendScheduledRecoverySmsUseCase>();
        Assert.Equal(
            OutboundSmsOutcome.Ignored,
            await useCase.ExecuteAsync(
                seed.ActionId,
                seed.TenantId,
                "blocked-after-stop",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PermanentDeliveryFailureIsVisibleAndDoesNotCreateRetryAction()
    {
        WorkflowSeed seed = await SeedWorkflowAsync("+14165550220", "+14165550221");
        string providerMessageSid;
        await using (AsyncServiceScope sendScope = fixture.Application.Services.CreateAsyncScope())
        {
            SendScheduledRecoverySmsUseCase useCase =
                sendScope.ServiceProvider.GetRequiredService<SendScheduledRecoverySmsUseCase>();
            Assert.Equal(
                OutboundSmsOutcome.Accepted,
                await useCase.ExecuteAsync(
                    seed.ActionId,
                    seed.TenantId,
                    "delivery-send",
                    new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                    TestContext.Current.CancellationToken));
            LeadRecoveryDbContext dbContext =
                sendScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            providerMessageSid = (await dbContext.Messages.IgnoreQueryFilters().SingleAsync(
                candidate => candidate.TenantId == seed.TenantId,
                TestContext.Current.CancellationToken)).ProviderMessageSid!;
        }

        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["MessageSid"] = providerMessageSid,
            ["MessageStatus"] = "undelivered",
            ["ErrorCode"] = "21610",
        };
        using HttpResponseMessage first = await PostSignedAsync(
            "/api/v1/webhooks/twilio/sms/status",
            form);
        using HttpResponseMessage duplicate = await PostSignedAsync(
            "/api/v1/webhooks/twilio/sms/status",
            form);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Message message = await verificationContext.Messages.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.TenantId == seed.TenantId,
            TestContext.Current.CancellationToken);
        Assert.Equal(MessageStatus.Failed, message.Status);
        Assert.Equal("21610", message.FailureCode);
        Assert.Equal(
            1,
            await verificationContext.ScheduledActions.IgnoreQueryFilters().CountAsync(
                candidate => candidate.TenantId == seed.TenantId,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await verificationContext.ExternalEventReceipts.CountAsync(
                candidate => candidate.TenantId == seed.TenantId &&
                    candidate.EventType == "MessageStatus",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidSmsSignatureIsRejectedBeforePersistence()
    {
        using HttpClient client = fixture.Application.CreateClient();
        using FormUrlEncodedContent content = new(new Dictionary<string, string>
        {
            ["MessageSid"] = $"SM{Guid.NewGuid():N}",
            ["From"] = "+14165550230",
            ["To"] = "+14165550231",
            ["Body"] = "hello",
        });
        content.Headers.Add("X-Twilio-Signature", "invalid");

        using HttpResponseMessage response = await client.PostAsync(
            "/api/v1/webhooks/twilio/sms/inbound",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidInboundForUnknownNumberCreatesOnlySystemReceiptAndAudit()
    {
        int leadsBefore;
        int messagesBefore;
        await using (AsyncServiceScope beforeScope =
            fixture.Application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext beforeContext =
                beforeScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            leadsBefore = await beforeContext.Leads.IgnoreQueryFilters().CountAsync(
                TestContext.Current.CancellationToken);
            messagesBefore = await beforeContext.Messages.IgnoreQueryFilters().CountAsync(
                TestContext.Current.CancellationToken);
        }

        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["MessageSid"] = $"SM{Guid.NewGuid():N}",
            ["From"] = "+14165550232",
            ["To"] = "+14165550233",
            ["Body"] = "Is anyone there?",
        };
        using HttpResponseMessage response = await PostSignedAsync(
            "/api/v1/webhooks/twilio/sms/inbound",
            form);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using AsyncServiceScope afterScope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext afterContext =
            afterScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Equal(
            leadsBefore,
            await afterContext.Leads.IgnoreQueryFilters().CountAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            messagesBefore,
            await afterContext.Messages.IgnoreQueryFilters().CountAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await afterContext.ExternalEventReceipts.CountAsync(
                candidate => candidate.TenantId == null &&
                    candidate.EventType == "InboundSms" &&
                    candidate.ExternalEventId ==
                        $"sha256:{ComputeSha256(form["MessageSid"])}",
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await afterContext.AuditEvents.CountAsync(
                candidate => candidate.TenantId == null &&
                    candidate.Action == "InboundSmsIgnored",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostgreSqlHangfireServerProcessesScheduledRecoveryJob()
    {
        WorkflowSeed seed = await SeedWorkflowAsync("+14165550240", "+14165550241");
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<TestBackgroundTenantContext>();
        builder.Services.AddScoped<ITenantContext>(services =>
            services.GetRequiredService<TestBackgroundTenantContext>());
        builder.Services.AddScoped<ITenantExecutionScope>(services =>
            services.GetRequiredService<TestBackgroundTenantContext>());
        builder.Services.AddInfrastructure(fixture.DatabaseConnectionString);
        builder.Services.AddLeadRecoveryHangfire(fixture.DatabaseConnectionString);
        builder.Services.AddSmsProvider(new SmsProviderOptions("fake", false, null, null));
        builder.Services.AddSingleton(new SmsWorkerOptions(
            new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMinutes(5)));
        builder.Services.AddHangfireServer(options =>
        {
            options.Queues = ["sms"];
            options.WorkerCount = 1;
        });

        using IHost host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            IBackgroundJobClient jobs =
                host.Services.GetRequiredService<IBackgroundJobClient>();
            _ = jobs.Enqueue<ScheduledRecoverySmsJob>(job => job.ExecuteAsync(
                seed.ActionId,
                seed.TenantId,
                "hangfire-integration",
                CancellationToken.None));

            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            ScheduledActionStatus status = ScheduledActionStatus.Pending;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using AsyncServiceScope scope =
                    fixture.Application.Services.CreateAsyncScope();
                status = await scope.ServiceProvider
                    .GetRequiredService<LeadRecoveryDbContext>()
                    .ScheduledActions
                    .IgnoreQueryFilters()
                    .Where(candidate => candidate.Id == seed.ActionId)
                    .Select(candidate => candidate.Status)
                    .SingleAsync(TestContext.Current.CancellationToken);
                if (status == ScheduledActionStatus.Completed)
                {
                    break;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    TestContext.Current.CancellationToken);
            }

            Assert.Equal(ScheduledActionStatus.Completed, status);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private async Task<WorkflowSeed> SeedWorkflowAsync(
        string businessPhone,
        string customerPhone)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid leadId = Guid.CreateVersion7();
        Guid actionId = Guid.CreateVersion7();
        Guid templateId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(-2);
        Tenant tenant = new(
            tenantId,
            "Alpha Test",
            $"sms-{tenantId:N}",
            "America/Toronto",
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
        ScheduledAction action = new(
            actionId,
            tenantId,
            leadId,
            "SendInitialRecoverySms",
            now,
            $"test:{actionId:N}",
            JsonSerializer.Serialize(new { schemaVersion = 1 }),
            now);
        MessageTemplate template = new(
            templateId,
            tenantId,
            "Initial recovery",
            SmsTemplatePurposes.InitialMissedCallRecovery,
            "{{BusinessName}} received your call. How can we help?",
            1,
            userId,
            now);
        template.Approve(userId, now);
        template.Activate();

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.Add(tenant);
        dbContext.TenantPhoneNumbers.Add(number);
        dbContext.Leads.Add(lead);
        dbContext.ScheduledActions.Add(action);
        dbContext.MessageTemplates.Add(template);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new WorkflowSeed(
            tenantId,
            leadId,
            actionId,
            templateId,
            businessPhone,
            customerPhone);
    }

    private async Task<HttpResponseMessage> PostSignedAsync(
        string path,
        IReadOnlyDictionary<string, string> form)
    {
        using HttpClient client = fixture.Application.CreateClient();
        using FormUrlEncodedContent content = new(form);
        content.Headers.Add("X-Twilio-Signature", ComputeSignature(path, form));
        return await client.PostAsync(path, content, TestContext.Current.CancellationToken);
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

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record WorkflowSeed(
        Guid TenantId,
        Guid LeadId,
        Guid ActionId,
        Guid TemplateId,
        string BusinessPhone,
        string CustomerPhone);

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

    private sealed class TestBackgroundTenantContext : ITenantContext, ITenantExecutionScope
    {
        private Guid? _tenantId;

        public Guid TenantId => _tenantId ?? throw new TenantContextUnavailableException();

        public IDisposable Begin(Guid tenantId)
        {
            if (_tenantId is not null)
            {
                throw new InvalidOperationException("A tenant scope is already active.");
            }

            _tenantId = tenantId;
            return new DelegateDisposable(() => _tenantId = null);
        }
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            dispose();
            _disposed = true;
        }
    }
}
