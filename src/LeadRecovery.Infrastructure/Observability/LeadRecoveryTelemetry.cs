using System.Diagnostics;
using System.Diagnostics.Metrics;

using LeadRecovery.Application.Observability;

namespace LeadRecovery.Infrastructure.Observability;

public static class LeadRecoveryTelemetry
{
    public const string ActivitySourceName = "LeadRecovery.Workflows";
    public const string MeterName = "LeadRecovery.Operations";
    public const string Version = "1.0.0";

    private static readonly ActivitySource ActivitySource =
        new(ActivitySourceName, Version);
    private static readonly Meter Meter = new(MeterName, Version);
    private static readonly Counter<long> JobExecutions = Meter.CreateCounter<long>(
        "leadrecovery.jobs.executions",
        unit: "{execution}");
    private static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "leadrecovery.jobs.duration",
        unit: "s");
    private static readonly Histogram<double> JobQueueDelay = Meter.CreateHistogram<double>(
        "leadrecovery.jobs.queue_delay",
        unit: "s");
    private static readonly Counter<long> ProviderRequests = Meter.CreateCounter<long>(
        "leadrecovery.provider.requests",
        unit: "{request}");
    private static readonly Histogram<double> ProviderDuration = Meter.CreateHistogram<double>(
        "leadrecovery.provider.duration",
        unit: "s");
    private static readonly Counter<long> AutomationActionsCancelled =
        Meter.CreateCounter<long>(
            "leadrecovery.automation.actions_cancelled",
            unit: "{action}");

    public static TelemetryOperation StartJob(
        string jobType,
        Guid tenantId,
        Guid scheduledActionId,
        string? traceParent,
        string? traceState)
    {
        string normalizedJobType = NormalizeTag(jobType, nameof(jobType));
        ActivityTagsCollection activityTags = new()
        {
            ["leadrecovery.job.type"] = normalizedJobType,
            ["leadrecovery.tenant.id"] = tenantId.ToString("N"),
            ["leadrecovery.scheduled_action.id"] = scheduledActionId.ToString("N"),
        };
        Activity? activity = WorkflowTelemetryContextCapture.TryParseParent(
            traceParent,
            traceState,
            out ActivityContext parentContext)
            ? ActivitySource.StartActivity(
                $"scheduled_action {normalizedJobType}",
                ActivityKind.Consumer,
                parentContext,
                activityTags)
            : ActivitySource.StartActivity(
                $"scheduled_action {normalizedJobType}",
                ActivityKind.Consumer,
                default(ActivityContext),
                activityTags);

        return new TelemetryOperation(
            activity,
            (outcome, durationSeconds) =>
            {
                KeyValuePair<string, object?> jobTypeTag =
                    new("job.type", normalizedJobType);
                KeyValuePair<string, object?> outcomeTag = new("outcome", outcome);
                JobExecutions.Add(1, jobTypeTag, outcomeTag);
                JobDuration.Record(durationSeconds, jobTypeTag, outcomeTag);
            });
    }

    public static TelemetryOperation StartProvider(
        string provider,
        string operation,
        Guid tenantId)
    {
        string normalizedProvider = NormalizeTag(provider, nameof(provider));
        string normalizedOperation = NormalizeTag(operation, nameof(operation));
        ActivityTagsCollection activityTags = new()
        {
            ["server.address"] = normalizedProvider,
            ["leadrecovery.provider.operation"] = normalizedOperation,
            ["leadrecovery.tenant.id"] = tenantId.ToString("N"),
        };
        Activity? activity = ActivitySource.StartActivity(
            $"provider {normalizedOperation}",
            ActivityKind.Client,
            Activity.Current?.Context ?? default,
            activityTags);
        return new TelemetryOperation(
            activity,
            (outcome, durationSeconds) =>
            {
                KeyValuePair<string, object?> providerTag =
                    new("provider", normalizedProvider);
                KeyValuePair<string, object?> operationTag =
                    new("operation", normalizedOperation);
                KeyValuePair<string, object?> tenantTag =
                    new("tenant.id", tenantId.ToString("N"));
                KeyValuePair<string, object?> outcomeTag = new("outcome", outcome);
                ProviderRequests.Add(
                    1,
                    providerTag,
                    operationTag,
                    tenantTag,
                    outcomeTag);
                ProviderDuration.Record(
                    durationSeconds,
                    providerTag,
                    operationTag,
                    tenantTag,
                    outcomeTag);
            });
    }

    public static void RecordQueueDelay(string jobType, TimeSpan delay)
    {
        string normalizedJobType = NormalizeTag(jobType, nameof(jobType));
        JobQueueDelay.Record(
            Math.Max(0, delay.TotalSeconds),
            new KeyValuePair<string, object?>("job.type", normalizedJobType));
    }

    public static void RecordAutomationCancellation(
        string switchScope,
        Guid tenantId,
        int actionCount)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(actionCount);

        if (actionCount == 0)
        {
            return;
        }

        AutomationActionsCancelled.Add(
            actionCount,
            new KeyValuePair<string, object?>(
                "automation.scope",
                NormalizeTag(switchScope, nameof(switchScope))),
            new KeyValuePair<string, object?>("tenant.id", tenantId.ToString("N")));
    }

    private static string NormalizeTag(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 100 ||
            normalized.Any(character => char.IsControl(character)))
        {
            throw new ArgumentException(
                "Telemetry tag values must be printable and at most 100 characters.",
                parameterName);
        }

        return normalized;
    }
}

public sealed class TelemetryOperation : IDisposable
{
    private readonly Activity? _activity;
    private readonly Action<string, double> _recordCompletion;
    private readonly long _startedAtTimestamp = Stopwatch.GetTimestamp();
    private bool _completed;

    internal TelemetryOperation(
        Activity? activity,
        Action<string, double> recordCompletion)
    {
        _activity = activity;
        _recordCompletion = recordCompletion;
    }

    public void Complete(string outcome, bool isError = false)
    {
        if (_completed)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        string normalizedOutcome = outcome.Trim();
        if (normalizedOutcome.Length > 100 ||
            normalizedOutcome.Any(character => char.IsControl(character)))
        {
            throw new ArgumentException(
                "The telemetry outcome must be printable and at most 100 characters.",
                nameof(outcome));
        }

        _completed = true;
        _activity?.SetTag("leadrecovery.outcome", normalizedOutcome);
        _activity?.SetStatus(
            isError ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        _recordCompletion(
            normalizedOutcome,
            Stopwatch.GetElapsedTime(_startedAtTimestamp).TotalSeconds);
    }

    public void Dispose()
    {
        if (!_completed)
        {
            Complete("UnhandledError", isError: true);
        }

        _activity?.Dispose();
    }
}
