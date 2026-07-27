using System.Diagnostics;

namespace LeadRecovery.Application.Observability;

public sealed record WorkflowTelemetryContext(
    string CorrelationId,
    string? TraceParent,
    string? TraceState);

public static class WorkflowTelemetryContextCapture
{
    public const int CorrelationIdMaximumLength = 100;
    public const int TraceParentMaximumLength = 55;
    public const int TraceStateMaximumLength = 512;

    public static WorkflowTelemetryContext Capture(string? preferredCorrelationId = null)
    {
        Activity? activity = Activity.Current;
        string correlationId = NormalizeCorrelationId(preferredCorrelationId) ??
            GetActivityCorrelationId(activity) ??
            $"generated:{Guid.CreateVersion7():N}";
        string? traceParent = activity is
        {
            IdFormat: ActivityIdFormat.W3C,
            Id.Length: <= TraceParentMaximumLength,
        }
            ? activity.Id
            : null;
        string? traceState = NormalizeTraceState(activity?.TraceStateString);
        return new WorkflowTelemetryContext(correlationId, traceParent, traceState);
    }

    public static bool TryParseParent(
        string? traceParent,
        string? traceState,
        out ActivityContext parentContext) =>
        !string.IsNullOrWhiteSpace(traceParent) &&
        ActivityContext.TryParse(
            traceParent,
            NormalizeTraceState(traceState),
            isRemote: true,
            out parentContext);

    public static string? NormalizeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length <= CorrelationIdMaximumLength &&
            normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.' or ':' or '/')
            ? normalized
            : null;
    }

    private static string? GetActivityCorrelationId(Activity? activity) =>
        activity is not null && activity.TraceId != default
            ? activity.TraceId.ToString()
            : null;

    private static string? NormalizeTraceState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length <= TraceStateMaximumLength &&
            normalized.All(character => character is >= '!' and <= '~')
            ? normalized
            : null;
    }
}
