using LeadRecovery.Api.Integrations.Twilio;
using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Messaging;

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
            .RequireRateLimiting("provider-webhook")
            .WithMetadata(new RequestSizeLimitAttribute(16_384));
        endpoints.MapPost(
                "/api/v1/webhooks/twilio/sms/inbound",
                HandleInboundSmsAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("provider-webhook")
            .WithMetadata(new RequestSizeLimitAttribute(32_768));
        endpoints.MapPost(
                "/api/v1/webhooks/twilio/sms/status",
                HandleSmsStatusAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("provider-webhook")
            .WithMetadata(new RequestSizeLimitAttribute(16_384));
        return endpoints;
    }

    private static async Task<IResult> HandleInboundSmsAsync(
        HttpContext context,
        TwilioSmsRequestAdapter adapter,
        ProcessInboundSmsUseCase useCase,
        CancellationToken cancellationToken)
    {
        TwilioSmsAdapterResult<InboundSmsWebhookEvent> adapted =
            await adapter.AdaptInboundAsync(context, cancellationToken);
        return await CompleteSmsRequestAsync(
            adapted,
            useCase.ExecuteAsync,
            cancellationToken);
    }

    private static async Task<IResult> HandleSmsStatusAsync(
        HttpContext context,
        TwilioSmsRequestAdapter adapter,
        ProcessDeliveryStatusUseCase useCase,
        CancellationToken cancellationToken)
    {
        TwilioSmsAdapterResult<DeliveryStatusWebhookEvent> adapted =
            await adapter.AdaptStatusAsync(context, cancellationToken);
        return await CompleteSmsRequestAsync(
            adapted,
            useCase.ExecuteAsync,
            cancellationToken);
    }

    private static async Task<IResult> CompleteSmsRequestAsync<TEvent, TOutcome>(
        TwilioSmsAdapterResult<TEvent> adapted,
        Func<TEvent, CancellationToken, Task<TOutcome>> execute,
        CancellationToken cancellationToken)
        where TEvent : class
    {
        switch (adapted.Outcome)
        {
            case TwilioSmsAdapterOutcome.ConfigurationUnavailable:
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Webhook validation is unavailable.");
            case TwilioSmsAdapterOutcome.InvalidSignature:
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            case TwilioSmsAdapterOutcome.InvalidPayload:
                return Results.BadRequest();
            case TwilioSmsAdapterOutcome.Accepted:
                _ = await execute(
                    adapted.WebhookEvent ?? throw new InvalidOperationException(
                        "An accepted Twilio request must contain an event."),
                    cancellationToken);
                return Results.NoContent();
            default:
                throw new InvalidOperationException("Unknown Twilio adapter outcome.");
        }
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
