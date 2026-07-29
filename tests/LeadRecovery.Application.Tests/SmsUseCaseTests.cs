using LeadRecovery.Application.Messaging;

namespace LeadRecovery.Application.Tests;

public sealed class SmsUseCaseTests
{
    [Theory]
    [InlineData(
        "https://webhooks.example.test",
        "https://webhooks.example.test/api/v1/webhooks/twilio/sms/status")]
    [InlineData(
        "https://webhooks.example.test/lead-recovery/",
        "https://webhooks.example.test/lead-recovery/api/v1/webhooks/twilio/sms/status")]
    public void StatusCallbackPreservesCanonicalBasePath(
        string baseUrl,
        string expected)
    {
        Uri result = SmsStatusCallbackUriBuilder.Build(
            baseUrl,
            isDevelopment: false);

        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://webhooks.example.test")]
    [InlineData("https://user@webhooks.example.test")]
    [InlineData("https://webhooks.example.test?tenant=one")]
    public void StatusCallbackRejectsUnsafeProductionBaseUrl(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() =>
            SmsStatusCallbackUriBuilder.Build(baseUrl, isDevelopment: false));
    }

    [Fact]
    public void TemplateRendererAppliesOnlyApprovedBoundedPlaceholders()
    {
        SmsTemplateRenderResult result = SmsTemplateRenderer.Render(
            "Thanks for calling {{BusinessName}}. Book at {{BookingUrl}}",
            "Northstar",
            "https://booking.example.test/northstar");

        Assert.True(result.IsValid);
        Assert.Equal(
            "Thanks for calling Northstar. Book at https://booking.example.test/northstar",
            result.Body);
    }

    [Fact]
    public void TemplateRendererRejectsUnsupportedAndOverlongRenderedContent()
    {
        SmsTemplateRenderResult unsupported = SmsTemplateRenderer.Render(
            "Hello {{CustomerName}}",
            "Northstar",
            null);
        SmsTemplateRenderResult overlong = SmsTemplateRenderer.Render(
            new string('a', 1_599) + "{{BusinessName}}",
            "Northstar",
            null);

        Assert.False(unsupported.IsValid);
        Assert.Contains("unsupported", unsupported.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(overlong.IsValid);
        Assert.Contains("1600", overlong.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptedSendUsesPreparedProviderPayloadAndCompletes()
    {
        PreparedOutboundSms prepared = CreatePrepared();
        RecordingPersistence persistence = new(prepared, OutboundSmsOutcome.Accepted);
        RecordingSender sender = new(SmsSendResult.Accepted($"SM{Guid.NewGuid():N}"));
        SendScheduledRecoverySmsUseCase useCase = new(
            persistence,
            sender,
            new NoOpSmsMetrics(),
            new FixedTimeProvider());

        OutboundSmsOutcome outcome = await useCase.ExecuteAsync(
            prepared.ActionId,
            prepared.TenantId,
            "correlation",
            prepared.Request.StatusCallbackUri,
            TestContext.Current.CancellationToken);

        Assert.Equal(OutboundSmsOutcome.Accepted, outcome);
        Assert.Equal(prepared.Request, sender.Request);
        Assert.NotNull(persistence.CompletedResult);
    }

    [Fact]
    public async Task TransientProviderFailureRequestsRetryAndThrowsForHangfire()
    {
        PreparedOutboundSms prepared = CreatePrepared();
        RecordingPersistence persistence = new(
            prepared,
            OutboundSmsOutcome.RetryScheduled);
        RecordingSender sender = new(SmsSendResult.Transient("429", "busy"));
        SendScheduledRecoverySmsUseCase useCase = new(
            persistence,
            sender,
            new NoOpSmsMetrics(),
            new FixedTimeProvider());

        await Assert.ThrowsAsync<TransientSmsException>(() => useCase.ExecuteAsync(
            prepared.ActionId,
            prepared.TenantId,
            "correlation",
            prepared.Request.StatusCallbackUri,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManualPermanentProviderFailureCompletesAsFailedWithoutRetry()
    {
        PreparedOutboundSms prepared = CreatePrepared();
        RecordingManualPersistence persistence = new(
            prepared,
            OutboundSmsOutcome.PermanentlyFailed);
        RecordingSender sender = new(
            SmsSendResult.Permanent("21610", "Recipient cannot receive messages."));
        SendScheduledManualSmsUseCase useCase = new(
            persistence,
            sender,
            new NoOpSmsMetrics(),
            new FixedTimeProvider());

        OutboundSmsOutcome outcome = await useCase.ExecuteAsync(
            prepared.ActionId,
            prepared.TenantId,
            "correlation",
            prepared.Request.StatusCallbackUri,
            TestContext.Current.CancellationToken);

        Assert.Equal(OutboundSmsOutcome.PermanentlyFailed, outcome);
        Assert.Equal(SmsSendDisposition.PermanentFailure, persistence.CompletedResult?.Disposition);
        Assert.Equal(prepared.Request, sender.Request);
    }

    [Fact]
    public async Task WorkflowAcceptedSendUsesPreparedProviderPayloadAndCompletes()
    {
        PreparedOutboundSms prepared = CreatePrepared();
        RecordingWorkflowPersistence persistence = new(
            prepared,
            OutboundSmsOutcome.Accepted);
        RecordingSender sender = new(SmsSendResult.Accepted($"SM{Guid.NewGuid():N}"));
        SendScheduledWorkflowSmsUseCase useCase = new(
            persistence,
            sender,
            new NoOpSmsMetrics(),
            new FixedTimeProvider());

        OutboundSmsOutcome outcome = await useCase.ExecuteAsync(
            prepared.ActionId,
            prepared.TenantId,
            "correlation",
            prepared.Request.StatusCallbackUri,
            TestContext.Current.CancellationToken);

        Assert.Equal(OutboundSmsOutcome.Accepted, outcome);
        Assert.Equal(prepared.Request, sender.Request);
        Assert.NotNull(persistence.CompletedResult);
    }

    private static PreparedOutboundSms CreatePrepared()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid actionId = Guid.CreateVersion7();
        return new PreparedOutboundSms(
            tenantId,
            actionId,
            Guid.CreateVersion7(),
            new SmsSendRequest(
                tenantId,
                "+14165550100",
                "+14165550101",
                "Hello",
                $"scheduled-action:{actionId:N}",
                new Uri("https://webhooks.example.test/status")));
    }

    private sealed class RecordingSender(SmsSendResult result) : ISmsSender
    {
        public SmsSendRequest? Request { get; private set; }

        public Task<SmsSendResult> SendAsync(
            SmsSendRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingPersistence(
        PreparedOutboundSms prepared,
        OutboundSmsOutcome completionOutcome) : ISmsWorkflowPersistence
    {
        public SmsSendResult? CompletedResult { get; private set; }

        public Task<PreparedOutboundSms?> PrepareOutboundAsync(
            Guid actionId,
            Guid tenantId,
            string correlationId,
            DateTimeOffset now,
            Uri statusCallbackUri,
            CancellationToken cancellationToken) => Task.FromResult<PreparedOutboundSms?>(prepared);

        public Task<OutboundSmsOutcome> CompleteOutboundAsync(
            PreparedOutboundSms completedPrepared,
            SmsSendResult result,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CompletedResult = result;
            return Task.FromResult(completionOutcome);
        }

        public Task<InboundSmsOutcome> ProcessInboundAsync(
            InboundSmsWebhookEvent webhookEvent,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DeliveryStatusOutcome> ProcessDeliveryStatusAsync(
            DeliveryStatusWebhookEvent webhookEvent,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingManualPersistence(
        PreparedOutboundSms prepared,
        OutboundSmsOutcome completionOutcome) : IManualSmsWorkflowPersistence
    {
        public SmsSendResult? CompletedResult { get; private set; }

        public Task<PreparedOutboundSms?> PrepareManualOutboundAsync(
            Guid actionId,
            Guid tenantId,
            string correlationId,
            DateTimeOffset now,
            Uri statusCallbackUri,
            CancellationToken cancellationToken) => Task.FromResult<PreparedOutboundSms?>(prepared);

        public Task<OutboundSmsOutcome> CompleteManualOutboundAsync(
            PreparedOutboundSms completedPrepared,
            SmsSendResult result,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CompletedResult = result;
            return Task.FromResult(completionOutcome);
        }
    }

    private sealed class RecordingWorkflowPersistence(
        PreparedOutboundSms prepared,
        OutboundSmsOutcome completionOutcome) : IWorkflowSmsPersistence
    {
        public SmsSendResult? CompletedResult { get; private set; }

        public Task<PreparedOutboundSms?> PrepareWorkflowOutboundAsync(
            Guid actionId,
            Guid tenantId,
            string correlationId,
            DateTimeOffset now,
            Uri statusCallbackUri,
            CancellationToken cancellationToken) => Task.FromResult<PreparedOutboundSms?>(prepared);

        public Task<OutboundSmsOutcome> CompleteWorkflowOutboundAsync(
            PreparedOutboundSms completedPrepared,
            SmsSendResult result,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CompletedResult = result;
            return Task.FromResult(completionOutcome);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 7, 15, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoOpSmsMetrics : ISmsMetrics
    {
        public void RecordOutbound(OutboundSmsOutcome outcome)
        {
        }

        public void RecordInbound(InboundSmsOutcome outcome)
        {
        }

        public void RecordDelivery(DeliveryStatusOutcome outcome)
        {
        }
    }
}
