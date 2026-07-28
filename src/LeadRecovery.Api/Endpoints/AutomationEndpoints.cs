using System.Buffers.Binary;
using System.Security.Claims;

using LeadRecovery.Application.Authorization;
using LeadRecovery.Application.Automations;
using LeadRecovery.Contracts.Automations;

using Microsoft.AspNetCore.Antiforgery;

namespace LeadRecovery.Api.Endpoints;

internal static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder automation = endpoints.MapGroup("/api/v1/automation")
            .WithTags("Automation")
            .RequireAuthorization(AuthorizationPolicies.TenantMember);

        automation.MapGet(
            "/",
            async Task<IResult> (
                AutomationControlUseCase useCase,
                CancellationToken cancellationToken) =>
                Results.Ok(Map(
                    await useCase.GetAsync(cancellationToken),
                    cancelledActionCount: 0)));

        automation.MapPost(
                "/tenant",
                async Task<IResult> (
                    SetTenantAutomationRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    AutomationControlUseCase useCase,
                    CancellationToken cancellationToken) =>
                {
                    if (!await IsAntiforgeryRequestValid(antiforgery, context))
                    {
                        return Results.Problem(
                            title: "Antiforgery validation failed",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    if (!TryGetUserId(context.User, out Guid actorUserId))
                    {
                        return Results.Unauthorized();
                    }

                    if (!TryDecodeVersion(request.ExpectedRowVersion, out long version))
                    {
                        return Validation(
                            "expectedRowVersion",
                            "The expected tenant row version is invalid.");
                    }

                    if (!Enum.TryParse(
                            request.ReasonCode,
                            ignoreCase: true,
                            out AutomationControlReason reason) ||
                        !Enum.IsDefined(reason))
                    {
                        return Validation(
                            "reasonCode",
                            "The automation reason code is invalid.");
                    }

                    try
                    {
                        AutomationUpdateResult result = await useCase.SetTenantAsync(
                            request.Enabled,
                            version,
                            actorUserId,
                            reason,
                            context.TraceIdentifier,
                            cancellationToken);
                        AutomationStatusResponse response = Map(
                            result.Status,
                            result.CancelledActionCount);
                        return result.Outcome == AutomationUpdateOutcome.Conflict
                            ? Results.Conflict(response)
                            : Results.Ok(response);
                    }
                    catch (ArgumentException exception)
                    {
                        return Validation(exception.ParamName ?? "request", exception.Message);
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AutomationManager);

        return endpoints;
    }

    private static AutomationStatusResponse Map(
        AutomationStatus status,
        int cancelledActionCount) =>
        new(
            status.GlobalEnabled,
            status.TenantEnabled,
            status.EffectiveEnabled,
            EncodeVersion(status.TenantVersion),
            cancelledActionCount);

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId) &&
        userId != Guid.Empty;

    private static string EncodeVersion(long version)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, version);
        return Convert.ToBase64String(bytes);
    }

    private static bool TryDecodeVersion(string? token, out long version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[sizeof(long)];
        return Convert.TryFromBase64String(token, bytes, out int written) &&
            written == sizeof(long) &&
            (version = BinaryPrimitives.ReadInt64BigEndian(bytes)) >= 0;
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

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [message],
        });
}
