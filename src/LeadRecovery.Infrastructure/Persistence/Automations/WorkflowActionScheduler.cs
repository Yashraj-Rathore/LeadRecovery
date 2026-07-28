using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Observability;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Infrastructure.Persistence.Automations;

internal sealed class WorkflowActionScheduler(
    LeadRecoveryDbContext dbContext,
    IBusinessHoursScheduler businessHoursScheduler,
    IAutomationRuntimePolicy automationRuntimePolicy)
    : IWorkflowActionScheduler
{
    public async Task<bool> ScheduleFirstQualificationAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!CanSchedule(tenant, lead))
        {
            return false;
        }

        QualificationQuestionPolicy? first = workflow
            .GetQualificationQuestions()
            .FirstOrDefault();
        return first is not null && await ScheduleQualificationQuestionAsync(
            tenant,
            lead,
            workflow,
            first.Key,
            now,
            cancellationToken);
    }

    public async Task<bool> ScheduleQualificationQuestionAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        string questionKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!CanSchedule(tenant, lead))
        {
            return false;
        }

        QualificationQuestionPolicy question = workflow
            .GetQualificationQuestions()
            .Single(candidate => candidate.Key.Equals(
                questionKey,
                StringComparison.OrdinalIgnoreCase));
        string idempotencyKey =
            $"workflow:{lead.Id:N}:v{workflow.Version}:question:{question.Key}";
        DateTimeOffset scheduledFor = businessHoursScheduler.GetNextPermittedUtc(
            now,
            tenant.TimezoneId,
            workflow.GetBusinessHoursPolicy());
        return await AddIfMissing(
            lead,
            WorkflowScheduledActionTypes.SendQualificationQuestion,
            scheduledFor,
            idempotencyKey,
            new WorkflowScheduledActionPayload(
                1,
                $"qualification:{question.Key}",
                question.Key,
                null,
                null,
                lead.LastCustomerActivityAtUtc),
            now,
            cancellationToken);
    }

    public async Task<bool> ScheduleBookingLinkAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        string stage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!CanSchedule(tenant, lead))
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        string normalizedStage = stage.Trim();
        string idempotencyKey =
            $"workflow:{lead.Id:N}:v{workflow.Version}:booking:{normalizedStage}";
        DateTimeOffset scheduledFor = businessHoursScheduler.GetNextPermittedUtc(
            now,
            tenant.TimezoneId,
            workflow.GetBusinessHoursPolicy());
        return await AddIfMissing(
            lead,
            WorkflowScheduledActionTypes.SendBookingLink,
            scheduledFor,
            idempotencyKey,
            new WorkflowScheduledActionPayload(
                1,
                $"booking:{normalizedStage}",
                null,
                null,
                SmsTemplatePurposes.BookingLink,
                lead.LastCustomerActivityAtUtc),
            now,
            cancellationToken);
    }

    public async Task<int> ScheduleFollowUpsAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        string stage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!CanSchedule(tenant, lead))
        {
            return 0;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        int added = 0;
        foreach (FollowUpStepPolicy step in workflow.GetFollowUpSteps())
        {
            DateTimeOffset candidate = now.AddMinutes(step.DelayMinutes);
            DateTimeOffset scheduledFor = businessHoursScheduler.GetNextPermittedUtc(
                candidate,
                tenant.TimezoneId,
                workflow.GetBusinessHoursPolicy());
            string idempotencyKey =
                $"workflow:{lead.Id:N}:v{workflow.Version}:followup:{stage}:{step.Sequence}";
            if (await AddIfMissing(
                    lead,
                    WorkflowScheduledActionTypes.SendFollowUpSms,
                    scheduledFor,
                    idempotencyKey,
                    new WorkflowScheduledActionPayload(
                        1,
                        stage.Trim(),
                        null,
                        step.Sequence,
                        step.TemplatePurpose,
                        lead.LastCustomerActivityAtUtc),
                    now,
                    cancellationToken))
            {
                added++;
            }
        }

        return added;
    }

    public async Task<int> CancelPendingFollowUpsAsync(
        Guid leadId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<ScheduledAction> actions = await dbContext.ScheduledActions
            .Where(action =>
                action.LeadId == leadId &&
                action.ActionType == WorkflowScheduledActionTypes.SendFollowUpSms &&
                action.Status == ScheduledActionStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (ScheduledAction action in actions)
        {
            action.Cancel(now);
        }

        return actions.Count;
    }

    private async Task<bool> AddIfMissing(
        Lead lead,
        string actionType,
        DateTimeOffset scheduledFor,
        string idempotencyKey,
        WorkflowScheduledActionPayload payload,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool exists = await dbContext.ScheduledActions.AnyAsync(
            action => action.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (exists)
        {
            return false;
        }

        WorkflowTelemetryContext telemetry = WorkflowTelemetryContextCapture.Capture();
        dbContext.ScheduledActions.Add(new ScheduledAction(
            Guid.CreateVersion7(),
            lead.TenantId,
            lead.Id,
            actionType,
            scheduledFor,
            idempotencyKey,
            WorkflowScheduledActionPayloadSerializer.Serialize(payload),
            now,
            telemetry.CorrelationId,
            telemetry.TraceParent,
            telemetry.TraceState));
        return true;
    }

    private bool CanSchedule(Tenant tenant, Lead lead) =>
        automationRuntimePolicy.GlobalAutomationEnabled &&
        tenant.AutomationEnabled &&
        tenant.Status is TenantStatus.Trial or TenantStatus.Active &&
        lead.AutomationState == AutomationState.Active &&
        lead.Status is not (LeadStatus.Booked or LeadStatus.Closed or LeadStatus.ClosedWon);
}
