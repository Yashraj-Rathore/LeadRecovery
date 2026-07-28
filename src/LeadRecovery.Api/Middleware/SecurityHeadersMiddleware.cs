namespace LeadRecovery.Api.Middleware;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers["Content-Security-Policy"] =
                "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; " +
                "form-action 'self'";
            headers["Permissions-Policy"] =
                "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
