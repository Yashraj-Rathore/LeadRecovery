using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Conversations;

public sealed class Conversation : ITenantOwnedEntity
{
    private Conversation()
    {
    }

    public Conversation(
        Guid id,
        Guid tenantId,
        Guid leadId,
        ConversationChannel channel,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        LeadId = RequireId(leadId, nameof(leadId));
        Channel = RequireDefined(channel, nameof(channel));
        Status = ConversationStatus.Open;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid LeadId { get; private set; }

    public ConversationChannel Channel { get; private set; }

    public ConversationStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public void Close(DateTimeOffset closedAtUtc)
    {
        if (Status == ConversationStatus.Closed)
        {
            throw new InvalidOperationException("The conversation is already closed.");
        }

        DateTimeOffset utcTimestamp = RequireUtc(closedAtUtc, nameof(closedAtUtc));
        if (utcTimestamp < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(closedAtUtc),
                "The close timestamp cannot precede conversation creation.");
        }

        Status = ConversationStatus.Closed;
        ClosedAtUtc = utcTimestamp;
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
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

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", parameterName);
        }

        return value;
    }
}
