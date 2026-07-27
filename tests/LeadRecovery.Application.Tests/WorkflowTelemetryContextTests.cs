using System.Diagnostics;

using LeadRecovery.Application.Observability;

namespace LeadRecovery.Application.Tests;

public sealed class WorkflowTelemetryContextTests
{
    [Fact]
    public void CaptureUsesSafeCorrelationAndCurrentW3CContext()
    {
        using Activity activity = new Activity("webhook")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        WorkflowTelemetryContext context =
            WorkflowTelemetryContextCapture.Capture(" request:abc-123 ");

        Assert.Equal("request:abc-123", context.CorrelationId);
        Assert.Equal(activity.Id, context.TraceParent);
        Assert.True(WorkflowTelemetryContextCapture.TryParseParent(
            context.TraceParent,
            context.TraceState,
            out ActivityContext parsed));
        Assert.Equal(activity.TraceId, parsed.TraceId);
        Assert.True(parsed.IsRemote);
    }

    [Fact]
    public void CaptureRejectsUnsafeCorrelationAndFallsBackToTraceId()
    {
        using Activity activity = new Activity("webhook")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        WorkflowTelemetryContext context =
            WorkflowTelemetryContextCapture.Capture("caller@example.test +14165550199");

        Assert.Equal(activity.TraceId.ToString(), context.CorrelationId);
        Assert.DoesNotContain("caller@example.test", context.CorrelationId, StringComparison.Ordinal);
        Assert.DoesNotContain("14165550199", context.CorrelationId, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseParentRejectsInvalidContext()
    {
        Assert.False(WorkflowTelemetryContextCapture.TryParseParent(
            "customer-message",
            null,
            out _));
    }
}
