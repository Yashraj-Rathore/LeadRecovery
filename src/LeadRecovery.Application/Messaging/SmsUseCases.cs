namespace LeadRecovery.Application.Messaging;

public sealed class TransientSmsException(string message) : Exception(message);

public sealed class SendScheduledRecoverySmsUseCase(
    ISmsWorkflowPersistence persistence,
    ISmsSender sender,
    ISmsMetrics metrics,
    TimeProvider timeProvider)
{
    public async Task<OutboundSmsOutcome> ExecuteAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        Uri statusCallbackUri,
        CancellationToken cancellationToken)
    {
        PreparedOutboundSms? prepared = await persistence.PrepareOutboundAsync(
            actionId,
            tenantId,
            correlationId,
            timeProvider.GetUtcNow(),
            statusCallbackUri,
            cancellationToken);
        if (prepared is null)
        {
            metrics.RecordOutbound(OutboundSmsOutcome.Ignored);
            return OutboundSmsOutcome.Ignored;
        }

        SmsSendResult result = await sender.SendAsync(prepared.Request, cancellationToken);
        OutboundSmsOutcome outcome = await persistence.CompleteOutboundAsync(
            prepared,
            result,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        metrics.RecordOutbound(outcome);
        if (outcome == OutboundSmsOutcome.RetryScheduled)
        {
            throw new TransientSmsException("The SMS provider reported a transient failure.");
        }

        return outcome;
    }
}

public sealed class SendScheduledManualSmsUseCase(
    IManualSmsWorkflowPersistence persistence,
    ISmsSender sender,
    ISmsMetrics metrics,
    TimeProvider timeProvider)
{
    public async Task<OutboundSmsOutcome> ExecuteAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        Uri statusCallbackUri,
        CancellationToken cancellationToken)
    {
        PreparedOutboundSms? prepared = await persistence.PrepareManualOutboundAsync(
            actionId,
            tenantId,
            correlationId,
            timeProvider.GetUtcNow(),
            statusCallbackUri,
            cancellationToken);
        if (prepared is null)
        {
            metrics.RecordOutbound(OutboundSmsOutcome.Ignored);
            return OutboundSmsOutcome.Ignored;
        }

        SmsSendResult result = await sender.SendAsync(prepared.Request, cancellationToken);
        OutboundSmsOutcome outcome = await persistence.CompleteManualOutboundAsync(
            prepared,
            result,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        metrics.RecordOutbound(outcome);
        if (outcome == OutboundSmsOutcome.RetryScheduled)
        {
            throw new TransientSmsException(
                result.FailureDescription ?? "The SMS provider is temporarily unavailable.");
        }

        return outcome;
    }
}

public sealed class SendScheduledWorkflowSmsUseCase(
    IWorkflowSmsPersistence persistence,
    ISmsSender sender,
    ISmsMetrics metrics,
    TimeProvider timeProvider)
{
    public async Task<OutboundSmsOutcome> ExecuteAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        Uri statusCallbackUri,
        CancellationToken cancellationToken)
    {
        PreparedOutboundSms? prepared = await persistence.PrepareWorkflowOutboundAsync(
            actionId,
            tenantId,
            correlationId,
            timeProvider.GetUtcNow(),
            statusCallbackUri,
            cancellationToken);
        if (prepared is null)
        {
            metrics.RecordOutbound(OutboundSmsOutcome.Ignored);
            return OutboundSmsOutcome.Ignored;
        }

        SmsSendResult result = await sender.SendAsync(prepared.Request, cancellationToken);
        OutboundSmsOutcome outcome = await persistence.CompleteWorkflowOutboundAsync(
            prepared,
            result,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        metrics.RecordOutbound(outcome);
        if (outcome == OutboundSmsOutcome.RetryScheduled)
        {
            throw new TransientSmsException(
                result.FailureDescription ?? "The SMS provider is temporarily unavailable.");
        }

        return outcome;
    }
}

public sealed class ProcessInboundSmsUseCase(
    ISmsWorkflowPersistence persistence,
    ISmsMetrics metrics,
    TimeProvider timeProvider)
{
    public async Task<InboundSmsOutcome> ExecuteAsync(
        InboundSmsWebhookEvent webhookEvent,
        CancellationToken cancellationToken)
    {
        InboundSmsOutcome outcome = await persistence.ProcessInboundAsync(
            webhookEvent,
            timeProvider.GetUtcNow(),
            cancellationToken);
        metrics.RecordInbound(outcome);
        return outcome;
    }
}

public sealed class ProcessDeliveryStatusUseCase(
    ISmsWorkflowPersistence persistence,
    ISmsMetrics metrics,
    TimeProvider timeProvider)
{
    public async Task<DeliveryStatusOutcome> ExecuteAsync(
        DeliveryStatusWebhookEvent webhookEvent,
        CancellationToken cancellationToken)
    {
        DeliveryStatusOutcome outcome = await persistence.ProcessDeliveryStatusAsync(
            webhookEvent,
            timeProvider.GetUtcNow(),
            cancellationToken);
        metrics.RecordDelivery(outcome);
        return outcome;
    }
}
