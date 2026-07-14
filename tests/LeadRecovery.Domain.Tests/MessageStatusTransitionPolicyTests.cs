using LeadRecovery.Domain.Conversations;

namespace LeadRecovery.Domain.Tests;

public sealed class MessageStatusTransitionPolicyTests
{
    public static TheoryData<MessageStatus, MessageStatus, bool> Transitions
    {
        get
        {
            HashSet<(MessageStatus Current, MessageStatus Target)> allowed =
            [
                (MessageStatus.Queued, MessageStatus.Sent),
                (MessageStatus.Queued, MessageStatus.Failed),
                (MessageStatus.Queued, MessageStatus.Suppressed),
                (MessageStatus.Sent, MessageStatus.Delivered),
                (MessageStatus.Sent, MessageStatus.Failed),
            ];
            TheoryData<MessageStatus, MessageStatus, bool> data = [];

            foreach (MessageStatus current in Enum.GetValues<MessageStatus>())
            {
                foreach (MessageStatus target in Enum.GetValues<MessageStatus>())
                {
                    data.Add(current, target, allowed.Contains((current, target)));
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void CanTransitionMatchesPolicy(
        MessageStatus current,
        MessageStatus target,
        bool expected) =>
        Assert.Equal(
            expected,
            MessageStatusTransitionPolicy.CanTransition(current, target));

    [Fact]
    public void CanTransitionRejectsUndefinedStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MessageStatusTransitionPolicy.CanTransition(
                (MessageStatus)99,
                MessageStatus.Sent));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MessageStatusTransitionPolicy.CanTransition(
                MessageStatus.Queued,
                (MessageStatus)99));
    }
}
