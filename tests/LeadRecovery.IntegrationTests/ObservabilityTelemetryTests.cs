using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using LeadRecovery.Application.Observability;
using LeadRecovery.Infrastructure.Observability;

namespace LeadRecovery.IntegrationTests;

public sealed class ObservabilityTelemetryTests
{
    private static readonly Guid TenantId =
        Guid.Parse("ee728524-e424-46f1-b55f-51a755f38cd8");

    [Fact]
    public void ScheduledJobAndProviderSpansContinueWebhookTrace()
    {
        using ActivityListener listener = new()
        {
            ShouldListenTo = source =>
                source.Name is "LeadRecovery.Tests" or LeadRecoveryTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using ActivitySource testSource = new("LeadRecovery.Tests");
        using Activity webhook = testSource.StartActivity(
            "twilio webhook",
            ActivityKind.Server)!;
        WorkflowTelemetryContext context =
            WorkflowTelemetryContextCapture.Capture("webhook:test-trace");

        Activity? jobActivity;
        Activity? providerActivity;
        using (TelemetryOperation job = LeadRecoveryTelemetry.StartJob(
            "AnalyzeLead",
            TenantId,
            Guid.CreateVersion7(),
            context.TraceParent,
            context.TraceState))
        {
            jobActivity = Activity.Current;
            using TelemetryOperation provider = LeadRecoveryTelemetry.StartProvider(
                "OpenAI",
                "lead_analysis",
                TenantId);
            providerActivity = Activity.Current;
            provider.Complete("Succeeded");
            job.Complete("Completed");
        }

        Assert.NotNull(jobActivity);
        Assert.NotNull(providerActivity);
        Assert.Equal(webhook.TraceId, jobActivity.TraceId);
        Assert.Equal(webhook.SpanId, jobActivity.ParentSpanId);
        Assert.Equal(jobActivity.TraceId, providerActivity.TraceId);
        Assert.Equal(jobActivity.SpanId, providerActivity.ParentSpanId);
    }

    [Fact]
    public void CoreMetricsContainBoundedOperationalTagsWithoutPii()
    {
        ConcurrentBag<Measurement> measurements = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == LeadRecoveryTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, tags.ToArray())));
        listener.Start();

        using (TelemetryOperation job = LeadRecoveryTelemetry.StartJob(
            "AnalyzeLead",
            TenantId,
            Guid.CreateVersion7(),
            null,
            null))
        {
            job.Complete("Completed");
        }

        using (TelemetryOperation provider = LeadRecoveryTelemetry.StartProvider(
            "OpenAI",
            "lead_analysis",
            TenantId))
        {
            provider.Complete("Succeeded");
        }

        LeadRecoveryTelemetry.RecordQueueDelay("AnalyzeLead", TimeSpan.FromSeconds(2));
        LeadRecoveryTelemetry.RecordAutomationCancellation("tenant", TenantId, 3);

        Assert.Contains(measurements, item =>
            item.Name == "leadrecovery.jobs.executions" && item.Value == 1);
        Assert.Contains(measurements, item =>
            item.Name == "leadrecovery.jobs.duration");
        Assert.Contains(measurements, item =>
            item.Name == "leadrecovery.jobs.queue_delay" && item.Value == 2);
        Assert.Contains(measurements, item =>
            item.Name == "leadrecovery.provider.requests" && item.Value == 1);
        Assert.Contains(measurements, item =>
            item.Name == "leadrecovery.provider.duration");
        Assert.Contains(measurements, item =>
            item.Name == "leadrecovery.automation.actions_cancelled" &&
            item.Value == 3 &&
            item.Tags.Any(tag =>
                tag.Key == "automation.scope" &&
                Equals(tag.Value, "tenant")));

        string telemetryText = string.Join(
            '|',
            measurements.SelectMany(item => item.Tags).Select(tag =>
                $"{tag.Key}={tag.Value}"));
        Assert.DoesNotContain("+14165550199", telemetryText, StringComparison.Ordinal);
        Assert.DoesNotContain("caller@example.test", telemetryText, StringComparison.Ordinal);
        Assert.DoesNotContain("customer message", telemetryText, StringComparison.Ordinal);
    }

    private sealed record Measurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);
}
