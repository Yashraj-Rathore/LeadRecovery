using System.Diagnostics.Metrics;

using LeadRecovery.Application.Messaging;

namespace LeadRecovery.Infrastructure.Messaging;

internal sealed class SmsMetrics : ISmsMetrics, IDisposable
{
    public const string MeterName = "LeadRecovery.Messaging.Sms";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _outbound;
    private readonly Counter<long> _inbound;
    private readonly Counter<long> _delivery;

    public SmsMetrics()
    {
        _outbound = _meter.CreateCounter<long>("leadrecovery.sms.outbound.outcomes");
        _inbound = _meter.CreateCounter<long>("leadrecovery.sms.inbound.outcomes");
        _delivery = _meter.CreateCounter<long>("leadrecovery.sms.delivery.outcomes");
    }

    public void RecordOutbound(OutboundSmsOutcome outcome) =>
        _outbound.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));

    public void RecordInbound(InboundSmsOutcome outcome) =>
        _inbound.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));

    public void RecordDelivery(DeliveryStatusOutcome outcome) =>
        _delivery.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));

    public void Dispose() => _meter.Dispose();
}
