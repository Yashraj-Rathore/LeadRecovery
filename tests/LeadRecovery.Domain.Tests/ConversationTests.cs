using LeadRecovery.Domain.Conversations;

namespace LeadRecovery.Domain.Tests;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 14, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorCreatesOpenSmsConversation()
    {
        Guid id = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid leadId = Guid.CreateVersion7();

        Conversation conversation = new(
            id,
            tenantId,
            leadId,
            ConversationChannel.Sms,
            CreatedAtUtc);

        Assert.Equal(id, conversation.Id);
        Assert.Equal(tenantId, conversation.TenantId);
        Assert.Equal(leadId, conversation.LeadId);
        Assert.Equal(ConversationChannel.Sms, conversation.Channel);
        Assert.Equal(ConversationStatus.Open, conversation.Status);
        Assert.Equal(CreatedAtUtc, conversation.CreatedAtUtc);
        Assert.Null(conversation.ClosedAtUtc);
    }

    [Fact]
    public void CloseRecordsTerminalTimestamp()
    {
        Conversation conversation = CreateConversation();
        DateTimeOffset closedAtUtc = CreatedAtUtc.AddMinutes(5);

        conversation.Close(closedAtUtc);

        Assert.Equal(ConversationStatus.Closed, conversation.Status);
        Assert.Equal(closedAtUtc, conversation.ClosedAtUtc);
        Assert.Throws<InvalidOperationException>(() => conversation.Close(closedAtUtc));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ConstructorRejectsEmptyRequiredId(
        bool emptyConversationId,
        bool emptyTenantId,
        bool emptyLeadId)
    {
        Guid conversationId = emptyConversationId ? Guid.Empty : Guid.CreateVersion7();
        Guid tenantId = emptyTenantId ? Guid.Empty : Guid.CreateVersion7();
        Guid leadId = emptyLeadId ? Guid.Empty : Guid.CreateVersion7();

        Assert.Throws<ArgumentException>(() => new Conversation(
            conversationId,
            tenantId,
            leadId,
            ConversationChannel.Sms,
            CreatedAtUtc));
    }

    [Fact]
    public void ConstructorRejectsInvalidChannelAndNonUtcTimestamp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Conversation(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            (ConversationChannel)99,
            CreatedAtUtc));

        Assert.Throws<ArgumentException>(() => new Conversation(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ConversationChannel.Sms,
            CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
    }

    [Fact]
    public void CloseRejectsBackwardsOrNonUtcTimestamp()
    {
        Conversation conversation = CreateConversation();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            conversation.Close(CreatedAtUtc.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() =>
            conversation.Close(CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
    }

    private static Conversation CreateConversation() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ConversationChannel.Sms,
            CreatedAtUtc);
}
