namespace LeadRecovery.Application.Integrations;

public interface ICallStatusMetrics
{
    void RecordSignatureRejected();

    void RecordOutcome(CallStatusProcessingOutcome outcome);
}
