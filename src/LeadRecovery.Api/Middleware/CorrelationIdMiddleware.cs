namespace LeadRecovery.Api.Middleware;

internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
        await next(context);
    }
}
