using System.Text.Json;

using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Observability;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Integrations;

public sealed class ProcessCallStatusWebhookUseCase(
    ICallStatusPersistence persistence,
    ITenantExecutionScope tenantExecutionScope,
    ICallStatusMetrics metrics,
    IAutomationRuntimePolicy automationRuntimePolicy,
    TimeProvider timeProvider)
{
    public const string RecoveryActionType = "SendInitialRecoverySms";

    public async Task<CallStatusProcessingOutcome> ExecuteAsync(
        CallStatusWebhookEvent webhookEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        CallStatusProcessingOutcome outcome = CallStatusProcessingOutcome.Duplicate;
        IDisposable? activeTenantScope = null;

        try
        {
            await persistence.ExecuteInTransactionAsync(
                async transactionCancellationToken =>
                {
                    DateTimeOffset now = timeProvider.GetUtcNow();
                    ExternalEventReceipt receipt = new(
                        Guid.CreateVersion7(),
                        null,
                        webhookEvent.Provider,
                        "CallStatus",
                        webhookEvent.ExternalEventId,
                        webhookEvent.PayloadHash,
                        now);
                    if (!await persistence.TryAddReceiptAsync(
                            receipt,
                            transactionCancellationToken))
                    {
                        outcome = CallStatusProcessingOutcome.Duplicate;
                        return;
                    }

                    TenantPhoneRecoveryRoute? route = await persistence.FindRouteAsync(
                        webhookEvent.Provider,
                        webhookEvent.DestinationPhoneE164,
                        transactionCancellationToken);
                    if (route is null)
                    {
                        outcome = CallStatusProcessingOutcome.IgnoredUnknownNumber;
                        CompleteIgnoredReceipt(receipt, outcome, webhookEvent, now);
                        return;
                    }

                    receipt.AssignTenant(route.TenantId);
                    if (!automationRuntimePolicy.GlobalAutomationEnabled)
                    {
                        outcome = CallStatusProcessingOutcome.IgnoredAutomationDisabled;
                        CompleteIgnoredReceipt(receipt, outcome, webhookEvent, now);
                        return;
                    }

                    if (!route.IsOperational)
                    {
                        outcome = CallStatusProcessingOutcome.IgnoredTenantInactive;
                        CompleteIgnoredReceipt(receipt, outcome, webhookEvent, now);
                        return;
                    }

                    if (!route.CanRecover(webhookEvent.CallStatus))
                    {
                        outcome = CallStatusProcessingOutcome.IgnoredStatus;
                        CompleteIgnoredReceipt(receipt, outcome, webhookEvent, now);
                        return;
                    }

                    activeTenantScope = tenantExecutionScope.Begin(route.TenantId);
                    Lead? latestLead = await persistence.FindLatestLeadAsync(
                        webhookEvent.CallerPhoneE164,
                        transactionCancellationToken);
                    if (latestLead?.AutomationState == AutomationState.SuppressedOptOut)
                    {
                        outcome = CallStatusProcessingOutcome.IgnoredAutomationDisabled;
                        CompleteIgnoredReceipt(receipt, outcome, webhookEvent, now);
                        return;
                    }

                    Lead lead;
                    if (latestLead is null || IsTerminal(latestLead.Status))
                    {
                        lead = new Lead(
                            Guid.CreateVersion7(),
                            route.TenantId,
                            webhookEvent.CallerPhoneE164,
                            LeadSource.MissedCall,
                            now);
                    }
                    else
                    {
                        lead = latestLead;
                        lead.RecordCustomerActivity(now);
                        if (lead.AutomationState != AutomationState.Active)
                        {
                            outcome = CallStatusProcessingOutcome.IgnoredAutomationDisabled;
                            CompleteIgnoredReceipt(receipt, outcome, webhookEvent, now);
                            return;
                        }
                    }

                    DateTimeOffset cooldownThreshold = now.AddSeconds(
                        -route.RecoveryCooldownSeconds);
                    if (await persistence.HasRecoveryActionSinceAsync(
                            webhookEvent.CallerPhoneE164,
                            cooldownThreshold,
                            transactionCancellationToken))
                    {
                        outcome = CallStatusProcessingOutcome.IgnoredCooldown;
                        CompleteIgnoredReceipt(receipt, outcome, webhookEvent, now);
                        return;
                    }

                    if (latestLead is null || !ReferenceEquals(lead, latestLead))
                    {
                        persistence.AddLead(lead);
                    }

                    WorkflowTelemetryContext telemetry =
                        WorkflowTelemetryContextCapture.Capture(
                            webhookEvent.CorrelationId);
                    ScheduledAction action = new(
                        Guid.CreateVersion7(),
                        route.TenantId,
                        lead.Id,
                        RecoveryActionType,
                        now.AddSeconds(route.InitialDelaySeconds),
                        $"twilio:{webhookEvent.ExternalEventId}",
                        JsonSerializer.Serialize(new { schemaVersion = 1 }),
                        now,
                        telemetry.CorrelationId,
                        telemetry.TraceParent,
                        telemetry.TraceState);
                    persistence.AddScheduledAction(action);

                    outcome = CallStatusProcessingOutcome.RecoveryScheduled;
                    receipt.MarkProcessed(outcome.ToString(), now);
                    persistence.AddAuditEvent(new AuditEvent(
                        Guid.CreateVersion7(),
                        route.TenantId,
                        "Integration",
                        webhookEvent.Provider,
                        "MissedCallRecoveryScheduled",
                        nameof(Lead),
                        lead.Id.ToString("N"),
                        webhookEvent.CorrelationId,
                        now,
                        afterJson: JsonSerializer.Serialize(new
                        {
                            result = outcome.ToString(),
                            scheduledActionId = action.Id,
                        })));
                },
                cancellationToken);
        }
        finally
        {
            activeTenantScope?.Dispose();
        }

        metrics.RecordOutcome(outcome);
        return outcome;
    }

    private void CompleteIgnoredReceipt(
        ExternalEventReceipt receipt,
        CallStatusProcessingOutcome outcome,
        CallStatusWebhookEvent webhookEvent,
        DateTimeOffset processedAtUtc)
    {
        receipt.MarkProcessed(outcome.ToString(), processedAtUtc);
        persistence.AddAuditEvent(new AuditEvent(
            Guid.CreateVersion7(),
            receipt.TenantId,
            "Integration",
            webhookEvent.Provider,
            "CallStatusWebhookIgnored",
            nameof(ExternalEventReceipt),
            receipt.Id.ToString("N"),
            webhookEvent.CorrelationId,
            processedAtUtc,
            afterJson: JsonSerializer.Serialize(new { result = outcome.ToString() })));
    }

    private static bool IsTerminal(LeadStatus status) =>
        status is LeadStatus.Booked or LeadStatus.Closed or LeadStatus.ClosedWon;
}
