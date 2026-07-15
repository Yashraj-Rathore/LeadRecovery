using LeadRecovery.Api.Integrations.Twilio;
using LeadRecovery.Application.Integrations;

using Microsoft.AspNetCore.Mvc;

namespace LeadRecovery.Api.Endpoints;

internal static class TwilioWebhookEndpoints
{
    public static IEndpointRouteBuilder MapTwilioWebhookEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(
                "/api/v1/webhooks/twilio/call-status",
                HandleCallStatusAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(16_384));
        return endpoints;
    }

    private static async Task<IResult> HandleCallStatusAsync(
        HttpContext context,
        TwilioCallStatusRequestAdapter adapter,
        ProcessCallStatusWebhookUseCase useCase,
        CancellationToken cancellationToken)
    {
        TwilioCallStatusAdapterResult adapted = await adapter.AdaptAsync(
            context,
            cancellationToken);
        switch (adapted.Outcome)
        {
            case TwilioCallStatusAdapterOutcome.ConfigurationUnavailable:
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Webhook validation is unavailable.");
            case TwilioCallStatusAdapterOutcome.InvalidSignature:
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            case TwilioCallStatusAdapterOutcome.InvalidPayload:
                return Results.BadRequest();
            case TwilioCallStatusAdapterOutcome.Accepted:
                _ = await useCase.ExecuteAsync(
                    adapted.WebhookEvent ?? throw new InvalidOperationException(
                        "An accepted Twilio request must contain an event."),
                    cancellationToken);
                return Results.NoContent();
            default:
                throw new InvalidOperationException("Unknown Twilio adapter outcome.");
        }
    }
}
