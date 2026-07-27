using System.Text.Json;

using LeadRecovery.Domain.Common;

namespace LeadRecovery.Domain.Automations;

public sealed class ScheduledAction : ITenantOwnedEntity
{
    private ScheduledAction()
    {
    }

    public ScheduledAction(
        Guid id,
        Guid tenantId,
        Guid leadId,
        string actionType,
        DateTimeOffset scheduledForUtc,
        string idempotencyKey,
        string payloadJson,
        DateTimeOffset createdAtUtc,
        string? correlationId = null,
        string? traceParent = null,
        string? traceState = null)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        LeadId = RequireId(leadId, nameof(leadId));
        ActionType = NormalizeRequired(
            actionType,
            ScheduledActionFieldLimits.ActionTypeMaximumLength,
            nameof(actionType));
        ScheduledForUtc = RequireUtc(scheduledForUtc, nameof(scheduledForUtc));
        IdempotencyKey = NormalizeRequired(
            idempotencyKey,
            ScheduledActionFieldLimits.IdempotencyKeyMaximumLength,
            nameof(idempotencyKey));
        PayloadJson = RequireJsonObject(payloadJson);
        CorrelationId = NormalizeCorrelationId(correlationId);
        TraceParent = NormalizeOptional(
            traceParent,
            ScheduledActionFieldLimits.TraceParentMaximumLength,
            nameof(traceParent));
        TraceState = NormalizeOptional(
            traceState,
            ScheduledActionFieldLimits.TraceStateMaximumLength,
            nameof(traceState));
        if (TraceParent is null && TraceState is not null)
        {
            throw new ArgumentException(
                "Trace state requires a trace parent.",
                nameof(traceState));
        }

        Status = ScheduledActionStatus.Pending;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid LeadId { get; private set; }

    public string ActionType { get; private set; } = string.Empty;

    public DateTimeOffset ScheduledForUtc { get; private set; }

    public ScheduledActionStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = string.Empty;

    public string? LastError { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? TraceParent { get; private set; }

    public string? TraceState { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Start(DateTimeOffset startedAtUtc)
    {
        EnsureCanTransitionTo(ScheduledActionStatus.Running);
        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            startedAtUtc,
            nameof(startedAtUtc));

        AttemptCount = checked(AttemptCount + 1);
        Status = ScheduledActionStatus.Running;
        UpdatedAtUtc = utcTimestamp;
    }

    public void Defer(
        DateTimeOffset scheduledForUtc,
        string reason,
        DateTimeOffset deferredAtUtc)
    {
        if (Status != ScheduledActionStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending action can be deferred.");
        }

        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            deferredAtUtc,
            nameof(deferredAtUtc));
        DateTimeOffset nextScheduledForUtc = RequireUtc(
            scheduledForUtc,
            nameof(scheduledForUtc));
        if (nextScheduledForUtc <= utcTimestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scheduledForUtc),
                "A deferred action must move to a future time.");
        }

        ScheduledForUtc = nextScheduledForUtc;
        LastError = NormalizeRequired(
            reason,
            ScheduledActionFieldLimits.LastErrorMaximumLength,
            nameof(reason));
        UpdatedAtUtc = utcTimestamp;
    }

    public void Retry(
        DateTimeOffset scheduledForUtc,
        string lastError,
        DateTimeOffset retriedAtUtc)
    {
        EnsureCanTransitionTo(ScheduledActionStatus.Pending);
        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            retriedAtUtc,
            nameof(retriedAtUtc));
        DateTimeOffset nextScheduledForUtc = RequireUtc(
            scheduledForUtc,
            nameof(scheduledForUtc));
        if (nextScheduledForUtc < utcTimestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scheduledForUtc),
                "A retry cannot be scheduled before the retry decision.");
        }

        string normalizedError = NormalizeRequired(
            lastError,
            ScheduledActionFieldLimits.LastErrorMaximumLength,
            nameof(lastError));

        ScheduledForUtc = nextScheduledForUtc;
        LastError = normalizedError;
        Status = ScheduledActionStatus.Pending;
        UpdatedAtUtc = utcTimestamp;
    }

    public void Complete(DateTimeOffset completedAtUtc) =>
        TransitionTo(ScheduledActionStatus.Completed, completedAtUtc);

    public void Fail(string lastError, DateTimeOffset failedAtUtc)
    {
        EnsureCanTransitionTo(ScheduledActionStatus.Failed);
        string normalizedError = NormalizeRequired(
            lastError,
            ScheduledActionFieldLimits.LastErrorMaximumLength,
            nameof(lastError));
        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            failedAtUtc,
            nameof(failedAtUtc));

        LastError = normalizedError;
        Status = ScheduledActionStatus.Failed;
        UpdatedAtUtc = utcTimestamp;
    }

    public void Cancel(DateTimeOffset cancelledAtUtc) =>
        TransitionTo(ScheduledActionStatus.Cancelled, cancelledAtUtc);

    private void TransitionTo(
        ScheduledActionStatus target,
        DateTimeOffset changedAtUtc)
    {
        EnsureCanTransitionTo(target);
        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            changedAtUtc,
            nameof(changedAtUtc));
        Status = target;
        UpdatedAtUtc = utcTimestamp;
    }

    private void EnsureCanTransitionTo(ScheduledActionStatus target)
    {
        if (!ScheduledActionStatusTransitionPolicy.CanTransition(Status, target))
        {
            throw new InvalidOperationException(
                $"A scheduled action cannot transition from {Status} to {target}.");
        }
    }

    private DateTimeOffset RequireCurrentOrLaterUtc(
        DateTimeOffset value,
        string parameterName)
    {
        DateTimeOffset utcValue = RequireUtc(value, parameterName);
        if (utcValue < UpdatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The update timestamp cannot move backwards.");
        }

        return utcValue;
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
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

        string normalized = value.Trim();
        if (normalized.Length > maximumLength ||
            normalized.Any(character => char.IsControl(character)))
        {
            throw new ArgumentException(
                $"The value must be printable and cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeCorrelationId(string? value)
    {
        string? normalized = NormalizeOptional(
            value,
            ScheduledActionFieldLimits.CorrelationIdMaximumLength,
            nameof(value));
        if (normalized is not null && normalized.Any(character =>
            !char.IsAsciiLetterOrDigit(character) &&
            character is not ('-' or '_' or '.' or ':' or '/')))
        {
            throw new ArgumentException(
                "Correlation IDs may contain only ASCII letters, digits, '-', '_', '.', ':', or '/'.",
                nameof(value));
        }

        return normalized;
    }

    private static string RequireJsonObject(string? payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (payloadJson.Length > ScheduledActionFieldLimits.PayloadJsonMaximumLength)
        {
            throw new ArgumentException(
                $"The payload cannot exceed " +
                $"{ScheduledActionFieldLimits.PayloadJsonMaximumLength} characters.",
                nameof(payloadJson));
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "The payload must be a JSON object.",
                    nameof(payloadJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The payload must contain valid JSON.",
                nameof(payloadJson),
                exception);
        }

        return payloadJson;
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
