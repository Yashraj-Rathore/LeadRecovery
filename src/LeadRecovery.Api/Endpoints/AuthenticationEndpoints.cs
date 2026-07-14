using LeadRecovery.Api.Identity;
using LeadRecovery.Application.Authorization;
using LeadRecovery.Contracts.Authentication;

using Microsoft.AspNetCore.Antiforgery;

namespace LeadRecovery.Api.Endpoints;

internal static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder auth = endpoints.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            {
                AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new CsrfTokenResponse(
                    tokens.RequestToken ?? throw new InvalidOperationException(
                        "An antiforgery request token was not generated.")));
            })
            .AllowAnonymous();

        auth.MapPost(
                "/login",
                async Task<IResult> (
                    LoginRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    AuthenticationSessionService sessions,
                    CancellationToken cancellationToken) =>
                {
                    if (!await IsAntiforgeryRequestValid(antiforgery, context))
                    {
                        return Results.Problem(
                            title: "Antiforgery validation failed",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    AuthSessionResponse? session = await sessions.LoginAsync(
                        request.Email,
                        request.Password,
                        context.TraceIdentifier,
                        cancellationToken);
                    return session is null
                        ? Results.Problem(
                            title: "Authentication failed",
                            detail: "The email or password is invalid.",
                            statusCode: StatusCodes.Status401Unauthorized)
                        : Results.Ok(session);
                })
            .AllowAnonymous()
            .RequireRateLimiting("login");

        auth.MapGet(
                "/me",
                async Task<IResult> (
                    HttpContext context,
                    AuthenticationSessionService sessions,
                    CancellationToken cancellationToken) =>
                {
                    AuthSessionResponse? session = await sessions.GetCurrentAsync(
                        context.User,
                        cancellationToken);
                    return session is null ? Results.Unauthorized() : Results.Ok(session);
                })
            .RequireAuthorization(AuthorizationPolicies.TenantMember);

        auth.MapPost(
                "/logout",
                async Task<IResult> (
                    HttpContext context,
                    IAntiforgery antiforgery,
                    AuthenticationSessionService sessions,
                    CancellationToken cancellationToken) =>
                {
                    if (!await IsAntiforgeryRequestValid(antiforgery, context))
                    {
                        return Results.Problem(
                            title: "Antiforgery validation failed",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    await sessions.LogoutAsync(
                        context.User,
                        context.TraceIdentifier,
                        cancellationToken);
                    return Results.NoContent();
                })
            .RequireAuthorization(AuthorizationPolicies.TenantMember);

        return endpoints;
    }

    private static async Task<bool> IsAntiforgeryRequestValid(
        IAntiforgery antiforgery,
        HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
