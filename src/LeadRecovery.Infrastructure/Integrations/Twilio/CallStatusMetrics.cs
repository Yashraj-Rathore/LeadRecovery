using System.Diagnostics.Metrics;

using LeadRecovery.Application.Integrations;

namespace LeadRecovery.Infrastructure.Integrations.Twilio;

internal sealed class CallStatusMetrics : ICallStatusMetrics, IDisposable
{
    private readonly Meter _meter = new("LeadRecovery.Integrations.Twilio", "1.0.0");
    private readonly Counter<long> _outcomes;
    private readonly Counter<long> _signatureRejections;

    public CallStatusMetrics()
    {
        _outcomes = _meter.CreateCounter<long>(
            "leadrecovery.twilio.call_status.outcomes");
        _signatureRejections = _meter.CreateCounter<long>(
            "leadrecovery.twilio.signature_rejections");
    }

    public void RecordSignatureRejected() => _signatureRejections.Add(1);

    public void RecordOutcome(CallStatusProcessingOutcome outcome) =>
        _outcomes.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome.ToString()));

    public void Dispose() => _meter.Dispose();
}
