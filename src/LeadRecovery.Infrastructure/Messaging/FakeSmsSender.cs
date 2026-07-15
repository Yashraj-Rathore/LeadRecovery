using System.Security.Cryptography;
using System.Text;

using LeadRecovery.Application.Messaging;

namespace LeadRecovery.Infrastructure.Messaging;

internal sealed class FakeSmsSender : ISmsSender
{
    public Task<SmsSendResult> SendAsync(
        SmsSendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.IdempotencyKey)))
            .ToLowerInvariant();
        return Task.FromResult(SmsSendResult.Accepted($"SM{hash[..32]}"));
    }
}
