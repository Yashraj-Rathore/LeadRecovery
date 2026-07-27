using LeadRecovery.Domain.Automations;

namespace LeadRecovery.Domain.Tests;

public sealed class ScheduledActionTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorCreatesNormalizedPendingAction()
    {
        Guid id = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid leadId = Guid.CreateVersion7();

        ScheduledAction action = new(
            id,
            tenantId,
            leadId,
            " SendSms ",
            CreatedAtUtc.AddMinutes(5),
            " follow-up:1 ",
            "{}",
            CreatedAtUtc);

        Assert.Equal(id, action.Id);
        Assert.Equal(tenantId, action.TenantId);
        Assert.Equal(leadId, action.LeadId);
        Assert.Equal("SendSms", action.ActionType);
        Assert.Equal("follow-up:1", action.IdempotencyKey);
        Assert.Equal(ScheduledActionStatus.Pending, action.Status);
        Assert.Equal(0, action.AttemptCount);
        Assert.Null(action.LastError);
        Assert.Null(action.CorrelationId);
        Assert.Null(action.TraceParent);
        Assert.Null(action.TraceState);
        Assert.Equal(CreatedAtUtc, action.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, action.UpdatedAtUtc);
    }

    [Fact]
    public void ConstructorStoresNormalizedTelemetryContext()
    {
        ScheduledAction action = CreateAction(
            correlationId: " webhook:trace-123 ",
            traceParent: " 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01 ",
            traceState: " vendor=value ");

        Assert.Equal("webhook:trace-123", action.CorrelationId);
        Assert.Equal(
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            action.TraceParent);
        Assert.Equal("vendor=value", action.TraceState);
    }

    [Fact]
    public void ActionCanRunRetryRunAndComplete()
    {
        ScheduledAction action = CreateAction();
        DateTimeOffset firstAttemptUtc = CreatedAtUtc.AddMinutes(5);
        DateTimeOffset retryDecisionUtc = firstAttemptUtc.AddSeconds(1);
        DateTimeOffset retryDueUtc = retryDecisionUtc.AddMinutes(2);

        action.Start(firstAttemptUtc);
        action.Retry(retryDueUtc, " transient failure ", retryDecisionUtc);
        action.Start(retryDueUtc);
        action.Complete(retryDueUtc.AddSeconds(1));

        Assert.Equal(ScheduledActionStatus.Completed, action.Status);
        Assert.Equal(2, action.AttemptCount);
        Assert.Equal(retryDueUtc, action.ScheduledForUtc);
        Assert.Equal("transient failure", action.LastError);
        Assert.Throws<InvalidOperationException>(() =>
            action.Start(retryDueUtc.AddSeconds(2)));
    }

    [Fact]
    public void RunningActionCanFail()
    {
        ScheduledAction action = CreateAction();
        action.Start(CreatedAtUtc.AddMinutes(5));

        action.Fail(" permanent failure ", CreatedAtUtc.AddMinutes(6));

        Assert.Equal(ScheduledActionStatus.Failed, action.Status);
        Assert.Equal("permanent failure", action.LastError);
        Assert.Throws<InvalidOperationException>(() =>
            action.Retry(
                CreatedAtUtc.AddMinutes(8),
                "retry",
                CreatedAtUtc.AddMinutes(7)));
    }

    [Fact]
    public void PendingActionCanCancelAndBecomesTerminal()
    {
        ScheduledAction action = CreateAction();

        action.Cancel(CreatedAtUtc.AddMinutes(1));

        Assert.Equal(ScheduledActionStatus.Cancelled, action.Status);
        Assert.Throws<InvalidOperationException>(() =>
            action.Cancel(CreatedAtUtc.AddMinutes(2)));
    }

    [Fact]
    public void PendingActionCanBeDeferredWithoutConsumingAnAttempt()
    {
        ScheduledAction action = CreateAction();
        DateTimeOffset deferredAtUtc = CreatedAtUtc.AddMinutes(1);
        DateTimeOffset nextDueUtc = CreatedAtUtc.AddHours(1);

        action.Defer(nextDueUtc, " outside business hours ", deferredAtUtc);

        Assert.Equal(ScheduledActionStatus.Pending, action.Status);
        Assert.Equal(nextDueUtc, action.ScheduledForUtc);
        Assert.Equal("outside business hours", action.LastError);
        Assert.Equal(0, action.AttemptCount);
        Assert.Equal(deferredAtUtc, action.UpdatedAtUtc);
    }

    [Fact]
    public void DeferRequiresPendingStateAndFutureUtcDueTime()
    {
        ScheduledAction action = CreateAction();
        DateTimeOffset deferredAtUtc = CreatedAtUtc.AddMinutes(1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            action.Defer(deferredAtUtc, "later", deferredAtUtc));
        Assert.Throws<ArgumentException>(() => action.Defer(
            CreatedAtUtc.AddHours(1).ToOffset(TimeSpan.FromHours(-5)),
            "later",
            deferredAtUtc));

        action.Cancel(CreatedAtUtc.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() =>
            action.Defer(CreatedAtUtc.AddHours(1), "later", CreatedAtUtc.AddMinutes(3)));
    }

    [Fact]
    public void RetryRequiresFutureDueTimeAndPreservesStateOnFailure()
    {
        ScheduledAction action = CreateAction();
        action.Start(CreatedAtUtc.AddMinutes(5));

        Assert.Throws<ArgumentOutOfRangeException>(() => action.Retry(
            CreatedAtUtc.AddMinutes(5),
            "failure",
            CreatedAtUtc.AddMinutes(6)));

        Assert.Equal(ScheduledActionStatus.Running, action.Status);
        Assert.Equal(1, action.AttemptCount);
        Assert.Null(action.LastError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public void ConstructorRejectsInvalidPayload(string payload)
    {
        Assert.Throws<ArgumentException>(() => CreateAction(payloadJson: payload));
    }

    [Fact]
    public void ConstructorRejectsOversizedFieldsAndEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => CreateAction(
            actionType: new string('a', ScheduledActionFieldLimits.ActionTypeMaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateAction(
            idempotencyKey: new string(
                'a',
                ScheduledActionFieldLimits.IdempotencyKeyMaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateAction(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateAction(tenantId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateAction(leadId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateAction(
            correlationId: new string(
                'a',
                ScheduledActionFieldLimits.CorrelationIdMaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateAction(
            traceParent: new string(
                'a',
                ScheduledActionFieldLimits.TraceParentMaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateAction(
            traceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            traceState: new string(
                'a',
                ScheduledActionFieldLimits.TraceStateMaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateAction(
            traceState: "vendor=value"));
        Assert.Throws<ArgumentException>(() => CreateAction(
            correlationId: "unsafe customer@example.test"));
    }

    [Fact]
    public void ConstructorAndTransitionsRequireUtcMonotonicTimestamps()
    {
        Assert.Throws<ArgumentException>(() => CreateAction(
            scheduledForUtc: CreatedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        ScheduledAction action = CreateAction();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            action.Start(CreatedAtUtc.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() =>
            action.Start(CreatedAtUtc.ToOffset(TimeSpan.FromHours(-5))));
    }

    private static ScheduledAction CreateAction(
        Guid? id = null,
        Guid? tenantId = null,
        Guid? leadId = null,
        string actionType = "SendSms",
        DateTimeOffset? scheduledForUtc = null,
        string idempotencyKey = "follow-up:1",
        string payloadJson = "{}",
        string? correlationId = null,
        string? traceParent = null,
        string? traceState = null) =>
        new(
            id ?? Guid.CreateVersion7(),
            tenantId ?? Guid.CreateVersion7(),
            leadId ?? Guid.CreateVersion7(),
            actionType,
            scheduledForUtc ?? CreatedAtUtc.AddMinutes(5),
            idempotencyKey,
            payloadJson,
            CreatedAtUtc,
            correlationId,
            traceParent,
            traceState);
}
