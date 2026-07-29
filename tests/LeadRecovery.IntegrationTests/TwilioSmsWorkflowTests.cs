extern alias WorkerAssembly;

using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Hangfire;

using LeadRecovery.Application.Automations;
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
    private static int workflowPhoneSequence;

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
    public async Task RenderedTemplateOverProviderLimitFailsSafelyWithoutSending()
    {
        WorkflowSeed seed = await SeedWorkflowAsync(
            "+14165550205",
            "+14165550206",
            new string('a', 1_401) + "{{BusinessName}}",
            new string('B', 200));

        OutboundSmsOutcome outcome;
        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            SendScheduledRecoverySmsUseCase useCase =
                scope.ServiceProvider.GetRequiredService<SendScheduledRecoverySmsUseCase>();
            outcome = await useCase.ExecuteAsync(
                seed.ActionId,
                seed.TenantId,
                "integration-render-limit",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(OutboundSmsOutcome.Ignored, outcome);
        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        ScheduledAction action = await dbContext.ScheduledActions.IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.Id == seed.ActionId,
                TestContext.Current.CancellationToken);
        Assert.Equal(ScheduledActionStatus.Failed, action.Status);
        Assert.Equal(
            0,
            await dbContext.Messages.IgnoreQueryFilters().CountAsync(
                candidate => candidate.TenantId == seed.TenantId,
                TestContext.Current.CancellationToken));
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
    public async Task QualificationResponseStoresStructuredAnswerAndOffersBooking()
    {
        ConfigurableWorkflowSeed seed = await SeedConfigurableWorkflowAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["MessageSid"] = $"SM{Guid.NewGuid():N}",
            ["From"] = seed.CustomerPhone,
            ["To"] = seed.BusinessPhone,
            ["Body"] = "plumbing",
        };

        using HttpResponseMessage response = await PostSignedAsync(
            "/api/v1/webhooks/twilio/sms/inbound",
            form);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Lead lead = await dbContext.Leads.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.Id == seed.LeadId,
            TestContext.Current.CancellationToken);
        QualificationAnswer answer = await dbContext.QualificationAnswers
            .IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.LeadId == seed.LeadId,
                TestContext.Current.CancellationToken);
        ScheduledAction booking = await dbContext.ScheduledActions.IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.LeadId == seed.LeadId &&
                    candidate.ActionType == WorkflowScheduledActionTypes.SendBookingLink,
                TestContext.Current.CancellationToken);
        ScheduledAction priorFollowUp = await dbContext.ScheduledActions.IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.Id == seed.PendingFollowUpId,
                TestContext.Current.CancellationToken);

        Assert.Equal(LeadStatus.BookingOffered, lead.Status);
        Assert.Equal(QualificationAnswerOutcome.Accepted, answer.Outcome);
        Assert.Equal("Plumbing", answer.Value);
        Assert.Equal(ScheduledActionStatus.Pending, booking.Status);
        Assert.Equal(ScheduledActionStatus.Cancelled, priorFollowUp.Status);
    }

    [Theory]
    [InlineData("plumbing or HVAC", QualificationAnswerOutcome.Ambiguous)]
    [InlineData("something else", QualificationAnswerOutcome.Unknown)]
    public async Task UnresolvedQualificationRoutesToHumanAndCancelsAutomation(
        string responseBody,
        QualificationAnswerOutcome expectedOutcome)
    {
        ConfigurableWorkflowSeed seed = await SeedConfigurableWorkflowAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["MessageSid"] = $"SM{Guid.NewGuid():N}",
            ["From"] = seed.CustomerPhone,
            ["To"] = seed.BusinessPhone,
            ["Body"] = responseBody,
        };

        using HttpResponseMessage response = await PostSignedAsync(
            "/api/v1/webhooks/twilio/sms/inbound",
            form);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Lead lead = await dbContext.Leads.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.Id == seed.LeadId,
            TestContext.Current.CancellationToken);
        QualificationAnswer answer = await dbContext.QualificationAnswers
            .IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.LeadId == seed.LeadId,
                TestContext.Current.CancellationToken);
        ScheduledAction priorFollowUp = await dbContext.ScheduledActions.IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.Id == seed.PendingFollowUpId,
                TestContext.Current.CancellationToken);

        Assert.Equal(LeadStatus.NeedsHuman, lead.Status);
        Assert.Equal(LeadUrgency.CriticalReview, lead.Urgency);
        Assert.Equal(expectedOutcome, answer.Outcome);
        Assert.Null(answer.Value);
        Assert.Equal(ScheduledActionStatus.Cancelled, priorFollowUp.Status);
        Assert.Contains(
            await dbContext.AuditEvents.IgnoreQueryFilters()
                .Where(candidate => candidate.TenantId == seed.TenantId)
                .Select(candidate => candidate.AfterJson)
                .ToArrayAsync(TestContext.Current.CancellationToken),
            json => json is not null &&
                json.Contains("humanReviewAtUtc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BookingLinkSendsOnceSchedulesConfiguredMaximumAndRechecksClosure()
    {
        ConfigurableWorkflowSeed seed = await SeedConfigurableWorkflowAsync(
            bookingOffered: true);
        OutboundSmsOutcome first;
        OutboundSmsOutcome duplicate;
        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            SendScheduledWorkflowSmsUseCase useCase =
                scope.ServiceProvider.GetRequiredService<SendScheduledWorkflowSmsUseCase>();
            first = await useCase.ExecuteAsync(
                seed.PrimaryActionId,
                seed.TenantId,
                "booking-send",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
            duplicate = await useCase.ExecuteAsync(
                seed.PrimaryActionId,
                seed.TenantId,
                "booking-duplicate",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(OutboundSmsOutcome.Accepted, first);
        Assert.Equal(OutboundSmsOutcome.Ignored, duplicate);
        Guid followUpId;
        DateTimeOffset followUpDueUtc;
        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            using TenantClaimScope tenantClaim = new(scope.ServiceProvider, seed.TenantId);
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            Message bookingMessage = await dbContext.Messages.SingleAsync(
                candidate => candidate.LeadId == seed.LeadId,
                TestContext.Current.CancellationToken);
            ScheduledAction[] followUps = await dbContext.ScheduledActions
                .Where(candidate => candidate.LeadId == seed.LeadId &&
                    candidate.ActionType == WorkflowScheduledActionTypes.SendFollowUpSms)
                .OrderBy(candidate => candidate.ScheduledForUtc)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Book here: https://booking.example.test/acme", bookingMessage.Body);
            Assert.Equal(3, followUps.Length);
            Assert.All(followUps, action =>
                Assert.Equal(ScheduledActionStatus.Pending, action.Status));

            Lead lead = await dbContext.Leads.SingleAsync(
                candidate => candidate.Id == seed.LeadId,
                TestContext.Current.CancellationToken);
            lead.Book(DateTimeOffset.UtcNow);
            followUpId = followUps[0].Id;
            followUpDueUtc = followUps[0].ScheduledForUtc;
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            IWorkflowSmsPersistence persistence =
                scope.ServiceProvider.GetRequiredService<IWorkflowSmsPersistence>();
            PreparedOutboundSms? prepared = await persistence.PrepareWorkflowOutboundAsync(
                followUpId,
                seed.TenantId,
                "closed-recheck",
                followUpDueUtc,
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
            Assert.Null(prepared);
        }

        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext verification =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Equal(
            ScheduledActionStatus.Cancelled,
            await verification.ScheduledActions.IgnoreQueryFilters()
                .Where(candidate => candidate.Id == followUpId)
                .Select(candidate => candidate.Status)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await verification.Messages.IgnoreQueryFilters().CountAsync(
                candidate => candidate.LeadId == seed.LeadId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WorkflowActionOutsideBusinessHoursIsDeferredWithoutSending()
    {
        ConfigurableWorkflowSeed seed = await SeedConfigurableWorkflowAsync(
            bookingOffered: true,
            forceAfterHours: true);

        OutboundSmsOutcome outcome;
        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            SendScheduledWorkflowSmsUseCase useCase =
                scope.ServiceProvider.GetRequiredService<SendScheduledWorkflowSmsUseCase>();
            outcome = await useCase.ExecuteAsync(
                seed.PrimaryActionId,
                seed.TenantId,
                "after-hours",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(OutboundSmsOutcome.Ignored, outcome);
        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        ScheduledAction action = await dbContext.ScheduledActions.IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.Id == seed.PrimaryActionId,
                TestContext.Current.CancellationToken);
        Assert.Equal(ScheduledActionStatus.Pending, action.Status);
        Assert.True(action.ScheduledForUtc > DateTimeOffset.UtcNow);
        Assert.Equal("Deferred outside configured business hours.", action.LastError);
        Assert.Equal(
            0,
            await dbContext.Messages.IgnoreQueryFilters().CountAsync(
                candidate => candidate.LeadId == seed.LeadId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WorkflowActionIsCancelledAtExecutionAfterCustomerOptOut()
    {
        ConfigurableWorkflowSeed seed = await SeedConfigurableWorkflowAsync(
            bookingOffered: true);
        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            using TenantClaimScope tenantClaim = new(scope.ServiceProvider, seed.TenantId);
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Customer customer = new(
                Guid.CreateVersion7(),
                seed.TenantId,
                seed.CustomerPhone,
                now);
            customer.OptOut(now);
            dbContext.Customers.Add(customer);
            Lead lead = await dbContext.Leads.SingleAsync(
                candidate => candidate.Id == seed.LeadId,
                TestContext.Current.CancellationToken);
            lead.AssociateCustomer(customer.Id, now);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        OutboundSmsOutcome outcome;
        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            SendScheduledWorkflowSmsUseCase useCase =
                scope.ServiceProvider.GetRequiredService<SendScheduledWorkflowSmsUseCase>();
            outcome = await useCase.ExecuteAsync(
                seed.PrimaryActionId,
                seed.TenantId,
                "opted-out-recheck",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(OutboundSmsOutcome.Ignored, outcome);
        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext verification =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Equal(
            ScheduledActionStatus.Cancelled,
            await verification.ScheduledActions.IgnoreQueryFilters()
                .Where(candidate => candidate.Id == seed.PrimaryActionId)
                .Select(candidate => candidate.Status)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            0,
            await verification.Messages.IgnoreQueryFilters().CountAsync(
                candidate => candidate.LeadId == seed.LeadId,
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
        builder.Services.AddInfrastructure(
            fixture.DatabaseConnectionString,
            new AutomationRuntimeOptions(GlobalAutomationEnabled: true));
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
        string customerPhone,
        string templateBody = "{{BusinessName}} received your call. How can we help?",
        string tenantName = "Alpha Test")
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid leadId = Guid.CreateVersion7();
        Guid actionId = Guid.CreateVersion7();
        Guid templateId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(-2);
        Tenant tenant = new(
            tenantId,
            tenantName,
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
            templateBody,
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

    private async Task<ConfigurableWorkflowSeed> SeedConfigurableWorkflowAsync(
        bool bookingOffered = false,
        bool forceAfterHours = false)
    {
        int sequence = Interlocked.Increment(ref workflowPhoneSequence);
        string businessPhone = $"+1416556{sequence:D4}";
        string customerPhone = $"+1416557{sequence:D4}";
        Guid tenantId = Guid.CreateVersion7();
        Guid leadId = Guid.CreateVersion7();
        Guid primaryActionId = Guid.CreateVersion7();
        Guid pendingFollowUpId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(-2);
        Tenant tenant = new(
            tenantId,
            "Configurable Workflow Test",
            $"workflow-{tenantId:N}",
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
        if (bookingOffered)
        {
            lead.Qualify(true, null, now);
            lead.OfferBooking(now);
        }

        WorkflowDefinition workflow = new(
            Guid.CreateVersion7(),
            tenantId,
            "Tenant recovery workflow",
            1,
            "https://booking.example.test/acme",
            [
                new QualificationQuestionPolicy(
                    "service",
                    "Which service do you need?",
                    QualificationAnswerKind.Choice,
                    ["Plumbing", "HVAC"]),
            ],
            forceAfterHours
                ? new BusinessHoursPolicy(
                    [
                        new BusinessDayHours(
                            now.UtcDateTime.AddDays(1).DayOfWeek,
                            new TimeOnly(9, 0),
                            new TimeOnly(17, 0)),
                    ],
                    true)
                : new BusinessHoursPolicy(
                    Enum.GetValues<DayOfWeek>()
                        .Select(day => new BusinessDayHours(
                            day,
                            new TimeOnly(0, 0),
                            new TimeOnly(23, 59)))
                        .ToArray(),
                    true),
            [
                new FollowUpStepPolicy(1, 1, "WorkflowFollowUpOne"),
                new FollowUpStepPolicy(2, 5, "WorkflowFollowUpTwo"),
                new FollowUpStepPolicy(3, 15, "WorkflowFollowUpThree"),
            ],
            now);
        workflow.Activate(now);

        string primaryActionType = bookingOffered
            ? WorkflowScheduledActionTypes.SendBookingLink
            : WorkflowScheduledActionTypes.SendQualificationQuestion;
        WorkflowScheduledActionPayload primaryPayload = bookingOffered
            ? new(
                1,
                "booking:BookingOffered",
                null,
                null,
                SmsTemplatePurposes.BookingLink,
                null)
            : new(1, "qualification:service", "service", null, null, null);
        ScheduledAction primaryAction = new(
            primaryActionId,
            tenantId,
            leadId,
            primaryActionType,
            now,
            $"workflow-test:{primaryActionId:N}",
            WorkflowScheduledActionPayloadSerializer.Serialize(primaryPayload),
            now);
        if (!bookingOffered)
        {
            primaryAction.Start(now);
            primaryAction.Complete(now);
        }

        ScheduledAction pendingFollowUp = new(
            pendingFollowUpId,
            tenantId,
            leadId,
            WorkflowScheduledActionTypes.SendFollowUpSms,
            now.AddMinutes(20),
            $"workflow-test:{pendingFollowUpId:N}",
            WorkflowScheduledActionPayloadSerializer.Serialize(new(
                1,
                "qualification:service",
                null,
                1,
                "WorkflowFollowUpOne",
                null)),
            now);
        MessageTemplate[] templates =
        [
            CreateTemplate(
                tenantId,
                userId,
                "Booking link",
                SmsTemplatePurposes.BookingLink,
                "Book here: {{BookingUrl}}",
                now),
            CreateTemplate(
                tenantId,
                userId,
                "Follow-up one",
                "WorkflowFollowUpOne",
                "Are you still interested?",
                now),
            CreateTemplate(
                tenantId,
                userId,
                "Follow-up two",
                "WorkflowFollowUpTwo",
                "We can still help.",
                now),
            CreateTemplate(
                tenantId,
                userId,
                "Follow-up three",
                "WorkflowFollowUpThree",
                "Reply if you would like a call.",
                now),
        ];

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.Add(tenant);
        dbContext.TenantPhoneNumbers.Add(number);
        dbContext.Leads.Add(lead);
        dbContext.WorkflowDefinitions.Add(workflow);
        dbContext.ScheduledActions.Add(primaryAction);
        if (!bookingOffered)
        {
            dbContext.ScheduledActions.Add(pendingFollowUp);
        }

        dbContext.MessageTemplates.AddRange(templates);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new ConfigurableWorkflowSeed(
            tenantId,
            leadId,
            primaryActionId,
            pendingFollowUpId,
            businessPhone,
            customerPhone);
    }

    private static MessageTemplate CreateTemplate(
        Guid tenantId,
        Guid userId,
        string name,
        string purpose,
        string body,
        DateTimeOffset now)
    {
        MessageTemplate template = new(
            Guid.CreateVersion7(),
            tenantId,
            name,
            purpose,
            body,
            1,
            userId,
            now);
        template.Approve(userId, now);
        template.Activate();
        return template;
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

    private sealed record ConfigurableWorkflowSeed(
        Guid TenantId,
        Guid LeadId,
        Guid PrimaryActionId,
        Guid PendingFollowUpId,
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
