using LeadRecovery.Domain.Conversations;

namespace LeadRecovery.Domain.Tests;

public sealed class MessageTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 14, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReceiveInboundCreatesTerminalReceivedMessage()
    {
        Guid id = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid leadId = Guid.CreateVersion7();
        Guid conversationId = Guid.CreateVersion7();

        Message message = Message.ReceiveInbound(
            id,
            tenantId,
            leadId,
            conversationId,
            MessageKind.System,
            " Twilio ",
            " SM-inbound ",
            " inbound-event-key ",
            " Customer reply ",
            CreatedAtUtc);

        Assert.Equal(id, message.Id);
        Assert.Equal(tenantId, message.TenantId);
        Assert.Equal(leadId, message.LeadId);
        Assert.Equal(conversationId, message.ConversationId);
        Assert.Equal(MessageDirection.Inbound, message.Direction);
        Assert.Equal(MessageKind.System, message.Kind);
        Assert.Equal("Twilio", message.Provider);
        Assert.Equal("SM-inbound", message.ProviderMessageSid);
        Assert.Equal("inbound-event-key", message.ClientIdempotencyKey);
        Assert.Equal(" Customer reply ", message.Body);
        Assert.Equal(MessageStatus.Received, message.Status);
        Assert.Equal(CreatedAtUtc, message.CreatedAtUtc);
        Assert.Throws<InvalidOperationException>(() =>
            message.MarkFailed(null, null));
    }

    [Fact]
    public void QueueOutboundCreatesQueuedMessageWithOptionalActors()
    {
        Guid sentByUserId = Guid.CreateVersion7();
        Guid templateId = Guid.CreateVersion7();

        Message message = CreateOutbound(sentByUserId, templateId);

        Assert.Equal(MessageDirection.Outbound, message.Direction);
        Assert.Equal(MessageKind.Manual, message.Kind);
        Assert.Equal(MessageStatus.Queued, message.Status);
        Assert.Null(message.ProviderMessageSid);
        Assert.Equal(sentByUserId, message.SentByUserId);
        Assert.Equal(templateId, message.TemplateId);
        Assert.Null(message.SentAtUtc);
        Assert.Null(message.DeliveredAtUtc);
    }

    [Fact]
    public void OutboundMessageTransitionsThroughSentAndDelivered()
    {
        Message message = CreateOutbound();
        DateTimeOffset sentAtUtc = CreatedAtUtc.AddSeconds(1);
        DateTimeOffset deliveredAtUtc = sentAtUtc.AddSeconds(2);

        message.MarkSent(" SM-outbound ", sentAtUtc);
        message.MarkDelivered(deliveredAtUtc);

        Assert.Equal(MessageStatus.Delivered, message.Status);
        Assert.Equal("SM-outbound", message.ProviderMessageSid);
        Assert.Equal(sentAtUtc, message.SentAtUtc);
        Assert.Equal(deliveredAtUtc, message.DeliveredAtUtc);
        Assert.Throws<InvalidOperationException>(() =>
            message.MarkFailed("late", "terminal state"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedOrSentMessageCanFail(bool markSentFirst)
    {
        Message message = CreateOutbound();
        if (markSentFirst)
        {
            message.MarkSent("SM-failed", CreatedAtUtc.AddSeconds(1));
        }

        message.MarkFailed(" 30007 ", " Carrier rejected message ");

        Assert.Equal(MessageStatus.Failed, message.Status);
        Assert.Equal("30007", message.FailureCode);
        Assert.Equal("Carrier rejected message", message.FailureDescription);
    }

    [Fact]
    public void InvalidFailureDetailsDoNotPartiallyMutateMessage()
    {
        Message message = CreateOutbound();

        Assert.Throws<ArgumentException>(() => message.MarkFailed(
            "30007",
            new string('a', MessageFieldLimits.FailureDescriptionMaximumLength + 1)));

        Assert.Equal(MessageStatus.Queued, message.Status);
        Assert.Null(message.FailureCode);
        Assert.Null(message.FailureDescription);
    }

    [Fact]
    public void QueuedMessageCanBeSuppressed()
    {
        Message message = CreateOutbound();

        message.Suppress();

        Assert.Equal(MessageStatus.Suppressed, message.Status);
        Assert.Throws<InvalidOperationException>(() => message.Suppress());
    }

    [Fact]
    public void BodyPolicyAcceptsLimitAndRejectsInvalidContent()
    {
        Message accepted = CreateOutbound(
            body: new string('a', MessageFieldLimits.BodyMaximumLength));

        Assert.Equal(MessageFieldLimits.BodyMaximumLength, accepted.Body.Length);
        Assert.Throws<ArgumentException>(() => CreateOutbound(body: "   "));
        Assert.Throws<ArgumentException>(() => CreateOutbound(
            body: new string('a', MessageFieldLimits.BodyMaximumLength + 1)));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void QueueOutboundRejectsEmptyRequiredId(
        bool emptyMessageId,
        bool emptyTenantId,
        bool emptyLeadId,
        bool emptyConversationId)
    {
        Assert.Throws<ArgumentException>(() => Message.QueueOutbound(
            emptyMessageId ? Guid.Empty : Guid.CreateVersion7(),
            emptyTenantId ? Guid.Empty : Guid.CreateVersion7(),
            emptyLeadId ? Guid.Empty : Guid.CreateVersion7(),
            emptyConversationId ? Guid.Empty : Guid.CreateVersion7(),
            MessageKind.Automated,
            "Twilio",
            "key",
            "body",
            CreatedAtUtc));
    }

    [Fact]
    public void FactoriesRejectInvalidEnumsAndOptionalEmptyIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Message.QueueOutbound(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            (MessageKind)99,
            "Twilio",
            "key",
            "body",
            CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => CreateOutbound(sentByUserId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateOutbound(templateId: Guid.Empty));
    }

    [Fact]
    public void FactoriesAndTransitionsRejectInvalidTimestampsAndProviderSid()
    {
        Assert.Throws<ArgumentException>(() => Message.QueueOutbound(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            MessageKind.Automated,
            "Twilio",
            "key",
            "body",
            CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));

        Message message = CreateOutbound();
        Assert.Throws<ArgumentException>(() => message.MarkSent(" ", CreatedAtUtc));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            message.MarkSent("SM-backwards", CreatedAtUtc.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() =>
            message.MarkSent(
                "SM-offset",
                CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));

        message.MarkSent("SM-valid", CreatedAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            message.MarkDelivered(CreatedAtUtc.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() =>
            message.MarkDelivered(CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
    }

    private static Message CreateOutbound(
        Guid? sentByUserId = null,
        Guid? templateId = null,
        string body = "Outbound message") =>
        Message.QueueOutbound(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            MessageKind.Manual,
            "Twilio",
            $"key-{Guid.CreateVersion7():N}",
            body,
            CreatedAtUtc,
            sentByUserId,
            templateId);
}
