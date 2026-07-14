using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Conversations;

public sealed class Message : ITenantOwnedEntity
{
    private Message()
    {
    }

    private Message(
        Guid id,
        Guid tenantId,
        Guid leadId,
        Guid conversationId,
        MessageDirection direction,
        MessageKind kind,
        string provider,
        string? providerMessageSid,
        string clientIdempotencyKey,
        string body,
        MessageStatus status,
        DateTimeOffset createdAtUtc,
        Guid? sentByUserId,
        Guid? templateId)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        LeadId = RequireId(leadId, nameof(leadId));
        ConversationId = RequireId(conversationId, nameof(conversationId));
        Direction = RequireDefined(direction, nameof(direction));
        Kind = RequireDefined(kind, nameof(kind));
        Provider = NormalizeRequired(
            provider,
            MessageFieldLimits.ProviderMaximumLength,
            nameof(provider));
        ProviderMessageSid = NormalizeOptional(
            providerMessageSid,
            MessageFieldLimits.ProviderMessageSidMaximumLength,
            nameof(providerMessageSid));
        ClientIdempotencyKey = NormalizeRequired(
            clientIdempotencyKey,
            MessageFieldLimits.ClientIdempotencyKeyMaximumLength,
            nameof(clientIdempotencyKey));
        Body = RequireBody(body);
        Status = RequireDefined(status, nameof(status));
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        SentByUserId = RequireOptionalId(sentByUserId, nameof(sentByUserId));
        TemplateId = RequireOptionalId(templateId, nameof(templateId));
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid LeadId { get; private set; }

    public Guid ConversationId { get; private set; }

    public MessageDirection Direction { get; private set; }

    public MessageKind Kind { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string? ProviderMessageSid { get; private set; }

    public string ClientIdempotencyKey { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public MessageStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public string? FailureDescription { get; private set; }

    public Guid? SentByUserId { get; private set; }

    public Guid? TemplateId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public static Message ReceiveInbound(
        Guid id,
        Guid tenantId,
        Guid leadId,
        Guid conversationId,
        MessageKind kind,
        string provider,
        string providerMessageSid,
        string clientIdempotencyKey,
        string body,
        DateTimeOffset receivedAtUtc) =>
        new(
            id,
            tenantId,
            leadId,
            conversationId,
            MessageDirection.Inbound,
            kind,
            provider,
            NormalizeRequired(
                providerMessageSid,
                MessageFieldLimits.ProviderMessageSidMaximumLength,
                nameof(providerMessageSid)),
            clientIdempotencyKey,
            body,
            MessageStatus.Received,
            receivedAtUtc,
            null,
            null);

    public static Message QueueOutbound(
        Guid id,
        Guid tenantId,
        Guid leadId,
        Guid conversationId,
        MessageKind kind,
        string provider,
        string clientIdempotencyKey,
        string body,
        DateTimeOffset createdAtUtc,
        Guid? sentByUserId = null,
        Guid? templateId = null) =>
        new(
            id,
            tenantId,
            leadId,
            conversationId,
            MessageDirection.Outbound,
            kind,
            provider,
            null,
            clientIdempotencyKey,
            body,
            MessageStatus.Queued,
            createdAtUtc,
            sentByUserId,
            templateId);

    public void MarkSent(string providerMessageSid, DateTimeOffset sentAtUtc)
    {
        EnsureCanTransitionTo(MessageStatus.Sent);
        DateTimeOffset utcTimestamp = RequireUtc(sentAtUtc, nameof(sentAtUtc));
        if (utcTimestamp < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sentAtUtc),
                "The sent timestamp cannot precede message creation.");
        }

        ProviderMessageSid = NormalizeRequired(
            providerMessageSid,
            MessageFieldLimits.ProviderMessageSidMaximumLength,
            nameof(providerMessageSid));
        Status = MessageStatus.Sent;
        SentAtUtc = utcTimestamp;
    }

    public void MarkDelivered(DateTimeOffset deliveredAtUtc)
    {
        EnsureCanTransitionTo(MessageStatus.Delivered);
        DateTimeOffset utcTimestamp = RequireUtc(deliveredAtUtc, nameof(deliveredAtUtc));
        if (utcTimestamp < SentAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deliveredAtUtc),
                "The delivered timestamp cannot precede the sent timestamp.");
        }

        Status = MessageStatus.Delivered;
        DeliveredAtUtc = utcTimestamp;
    }

    public void MarkFailed(string? failureCode, string? failureDescription)
    {
        EnsureCanTransitionTo(MessageStatus.Failed);
        string? normalizedFailureCode = NormalizeOptional(
            failureCode,
            MessageFieldLimits.FailureCodeMaximumLength,
            nameof(failureCode));
        string? normalizedFailureDescription = NormalizeOptional(
            failureDescription,
            MessageFieldLimits.FailureDescriptionMaximumLength,
            nameof(failureDescription));

        FailureCode = normalizedFailureCode;
        FailureDescription = normalizedFailureDescription;
        Status = MessageStatus.Failed;
    }

    public void Suppress()
    {
        EnsureCanTransitionTo(MessageStatus.Suppressed);
        Status = MessageStatus.Suppressed;
    }

    private void EnsureCanTransitionTo(MessageStatus target)
    {
        if (!MessageStatusTransitionPolicy.CanTransition(Status, target))
        {
            throw new InvalidOperationException(
                $"A message cannot transition from {Status} to {target}.");
        }
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
    }

    private static Guid? RequireOptionalId(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An optional ID cannot be empty.", parameterName);
        }

        return value;
    }

    private static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static string RequireBody(string? body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (body.Length > MessageFieldLimits.BodyMaximumLength)
        {
            throw new ArgumentException(
                $"The message body cannot exceed {MessageFieldLimits.BodyMaximumLength} characters.",
                nameof(body));
        }

        return body;
    }

    private static string NormalizeRequired(
        string? value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NormalizeRequired(value, maximumLength, parameterName);
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", parameterName);
        }

        return value;
    }
}
