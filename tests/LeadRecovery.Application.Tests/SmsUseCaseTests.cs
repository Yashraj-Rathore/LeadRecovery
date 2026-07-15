using LeadRecovery.Application.Messaging;

namespace LeadRecovery.Application.Tests;

public sealed class SmsUseCaseTests
{
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

    private static PreparedOutboundSms CreatePrepared()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid actionId = Guid.CreateVersion7();
        return new PreparedOutboundSms(
            tenantId,
            actionId,
            Guid.CreateVersion7(),
            new SmsSendRequest(
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
