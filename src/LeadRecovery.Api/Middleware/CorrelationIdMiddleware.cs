using LeadRecovery.Application.Observability;

namespace LeadRecovery.Api.Middleware;

internal sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger,
    IHostEnvironment environment)
{
    private static readonly string ServiceVersion =
        typeof(CorrelationIdMiddleware).Assembly.GetName().Version?.ToString() ?? "unknown";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        WorkflowTelemetryContext telemetry = WorkflowTelemetryContextCapture.Capture();
        context.TraceIdentifier = telemetry.CorrelationId;
        context.Response.Headers["X-Correlation-ID"] = telemetry.CorrelationId;
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "LeadRecovery.Api",
            ["ServiceVersion"] = ServiceVersion,
            ["Environment"] = environment.EnvironmentName,
            ["CorrelationId"] = telemetry.CorrelationId,
        });
        await next(context);
    }
}
