using LeadRecovery.Domain.Automations;

namespace LeadRecovery.Domain.Tests;

public sealed class ScheduledActionStatusTransitionPolicyTests
{
    public static TheoryData<ScheduledActionStatus, ScheduledActionStatus, bool>
        Transitions => new()
        {
            { ScheduledActionStatus.Pending, ScheduledActionStatus.Pending, false },
            { ScheduledActionStatus.Pending, ScheduledActionStatus.Running, true },
            { ScheduledActionStatus.Pending, ScheduledActionStatus.Completed, false },
            { ScheduledActionStatus.Pending, ScheduledActionStatus.Failed, false },
            { ScheduledActionStatus.Pending, ScheduledActionStatus.Cancelled, true },
            { ScheduledActionStatus.Running, ScheduledActionStatus.Pending, true },
            { ScheduledActionStatus.Running, ScheduledActionStatus.Running, false },
            { ScheduledActionStatus.Running, ScheduledActionStatus.Completed, true },
            { ScheduledActionStatus.Running, ScheduledActionStatus.Failed, true },
            { ScheduledActionStatus.Running, ScheduledActionStatus.Cancelled, false },
            { ScheduledActionStatus.Completed, ScheduledActionStatus.Pending, false },
            { ScheduledActionStatus.Completed, ScheduledActionStatus.Running, false },
            { ScheduledActionStatus.Completed, ScheduledActionStatus.Completed, false },
            { ScheduledActionStatus.Completed, ScheduledActionStatus.Failed, false },
            { ScheduledActionStatus.Completed, ScheduledActionStatus.Cancelled, false },
            { ScheduledActionStatus.Failed, ScheduledActionStatus.Pending, false },
            { ScheduledActionStatus.Failed, ScheduledActionStatus.Running, false },
            { ScheduledActionStatus.Failed, ScheduledActionStatus.Completed, false },
            { ScheduledActionStatus.Failed, ScheduledActionStatus.Failed, false },
            { ScheduledActionStatus.Failed, ScheduledActionStatus.Cancelled, false },
            { ScheduledActionStatus.Cancelled, ScheduledActionStatus.Pending, false },
            { ScheduledActionStatus.Cancelled, ScheduledActionStatus.Running, false },
            { ScheduledActionStatus.Cancelled, ScheduledActionStatus.Completed, false },
            { ScheduledActionStatus.Cancelled, ScheduledActionStatus.Failed, false },
            { ScheduledActionStatus.Cancelled, ScheduledActionStatus.Cancelled, false },
        };

    [Theory]
    [MemberData(nameof(Transitions))]
    public void PolicyReturnsExpectedResult(
        ScheduledActionStatus current,
        ScheduledActionStatus target,
        bool expected)
    {
        Assert.Equal(
            expected,
            ScheduledActionStatusTransitionPolicy.CanTransition(current, target));
    }

    [Fact]
    public void PolicyRejectsUnknownStatuses()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScheduledActionStatusTransitionPolicy.CanTransition(
                (ScheduledActionStatus)99,
                ScheduledActionStatus.Pending));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScheduledActionStatusTransitionPolicy.CanTransition(
                ScheduledActionStatus.Pending,
                (ScheduledActionStatus)99));
    }
}
