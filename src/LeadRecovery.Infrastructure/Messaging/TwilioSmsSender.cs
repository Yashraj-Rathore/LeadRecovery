using System.Globalization;

using LeadRecovery.Application.Messaging;
using LeadRecovery.Infrastructure.Observability;

using Twilio.Clients;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace LeadRecovery.Infrastructure.Messaging;

internal sealed class TwilioSmsSender : ISmsSender
{
    private readonly ITwilioRestClient _client;

    public TwilioSmsSender(SmsProviderOptions options)
    {
        if (!options.UseTwilio ||
            string.IsNullOrWhiteSpace(options.AccountSid) ||
            string.IsNullOrWhiteSpace(options.AuthToken))
        {
            throw new InvalidOperationException(
                "Real Twilio SMS requires SMS_PROVIDER=twilio, ALLOW_REAL_SMS=true, " +
                "TWILIO_ACCOUNT_SID, and TWILIO_AUTH_TOKEN.");
        }

        _client = new TwilioRestClient(options.AccountSid, options.AuthToken);
    }

    public async Task<SmsSendResult> SendAsync(
        SmsSendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using TelemetryOperation telemetry = LeadRecoveryTelemetry.StartProvider(
            "Twilio",
            "sms_send",
            request.TenantId);
        try
        {
            MessageResource message = await MessageResource.CreateAsync(
                to: new PhoneNumber(request.ToPhoneE164),
                from: new PhoneNumber(request.FromPhoneE164),
                body: request.Body,
                statusCallback: request.StatusCallbackUri,
                client: _client);
            telemetry.Complete("Accepted");
            return SmsSendResult.Accepted(message.Sid);
        }
        catch (ApiException exception) when (
            exception.Status == 429 || exception.Status >= 500)
        {
            telemetry.Complete("TransientFailure", isError: true);
            return SmsSendResult.Transient(
                GetFailureCode(exception),
                "The provider is temporarily unavailable.");
        }
        catch (ApiException exception)
        {
            telemetry.Complete("PermanentFailure", isError: true);
            return SmsSendResult.Permanent(
                GetFailureCode(exception),
                "The provider rejected the message.");
        }
        catch (HttpRequestException)
        {
            telemetry.Complete("NetworkFailure", isError: true);
            return SmsSendResult.Transient("network", "The provider could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            telemetry.Complete("Timeout", isError: true);
            return SmsSendResult.Transient("timeout", "The provider request timed out.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            telemetry.Complete("Cancelled");
            throw;
        }
    }

    private static string GetFailureCode(ApiException exception) =>
        (exception.Code == 0 ? exception.Status : exception.Code)
            .ToString(CultureInfo.InvariantCulture);
}
