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
        Assert.Equal(CreatedAtUtc, action.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, action.UpdatedAtUtc);
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
        string payloadJson = "{}") =>
        new(
            id ?? Guid.CreateVersion7(),
            tenantId ?? Guid.CreateVersion7(),
            leadId ?? Guid.CreateVersion7(),
            actionType,
            scheduledForUtc ?? CreatedAtUtc.AddMinutes(5),
            idempotencyKey,
            payloadJson,
            CreatedAtUtc);
}
