using System.Data;
using System.Text.Json;

using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeadRecovery.Infrastructure.Persistence.Messaging;

internal sealed class WorkflowSmsPersistence(
    LeadRecoveryDbContext dbContext,
    ITenantExecutionScope tenantExecutionScope,
    IBusinessHoursScheduler businessHoursScheduler,
    IWorkflowActionScheduler actionScheduler)
    : IWorkflowSmsPersistence
{
    public async Task<PreparedOutboundSms?> PrepareWorkflowOutboundAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        DateTimeOffset now,
        Uri statusCallbackUri,
        CancellationToken cancellationToken)
    {
        using IDisposable tenantScope = tenantExecutionScope.Begin(tenantId);
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        ScheduledAction? action = await dbContext.ScheduledActions
            .FromSqlInterpolated(
                $"select * from scheduled_actions where id = {actionId} and tenant_id = {tenantId} for update")
            .SingleOrDefaultAsync(cancellationToken);
        if (action is null ||
            !WorkflowScheduledActionTypes.IsWorkflowSms(action.ActionType) ||
            action.Status != ScheduledActionStatus.Pending ||
            action.ScheduledForUtc > now ||
            !WorkflowScheduledActionPayloadSerializer.TryDeserialize(
                action.PayloadJson,
                out WorkflowScheduledActionPayload? payload))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        Tenant tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == tenantId,
            cancellationToken);
        Lead lead = await dbContext.Leads.SingleAsync(
            candidate => candidate.Id == action.LeadId,
            cancellationToken);
        WorkflowDefinition? workflow = await dbContext.WorkflowDefinitions
            .SingleOrDefaultAsync(candidate => candidate.IsActive, cancellationToken);
        TenantPhoneNumber? sendingNumber = await dbContext.TenantPhoneNumbers
            .Where(number => number.InboundSmsEnabled)
            .OrderByDescending(number => number.IsPrimary)
            .ThenBy(number => number.Id)
            .FirstOrDefaultAsync(cancellationToken);
        Customer? customer = await dbContext.Customers.SingleOrDefaultAsync(
            candidate => candidate.PhoneE164 == lead.PrimaryPhoneE164,
            cancellationToken);
        bool eligible = workflow is not null &&
            tenant.Status is TenantStatus.Trial or TenantStatus.Active &&
            tenant.AutomationEnabled &&
            lead.AutomationState == AutomationState.Active &&
            lead.Status is not (LeadStatus.Booked or LeadStatus.Closed or LeadStatus.ClosedWon) &&
            customer?.OptedOutAtUtc is null &&
            sendingNumber is not null;
        if (!eligible)
        {
            await Cancel(
                action,
                lead,
                correlationId,
                now,
                "ExecutionTimeEligibilityFailed",
                transaction,
                cancellationToken);
            return null;
        }

        WorkflowDefinition activeWorkflow = workflow!;
        DateTimeOffset permittedAt = businessHoursScheduler.GetNextPermittedUtc(
            now,
            tenant.TimezoneId,
            activeWorkflow.GetBusinessHoursPolicy());
        if (permittedAt > now)
        {
            action.Defer(permittedAt, "Deferred outside configured business hours.", now);
            AddAudit(
                lead,
                "WorkflowSmsDeferred",
                correlationId,
                now,
                new { actionId = action.Id, scheduledForUtc = permittedAt });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        ResolvedContent? content = await ResolveContent(
            action,
            payload!,
            activeWorkflow,
            lead,
            tenant,
            cancellationToken);
        if (content is null)
        {
            await Cancel(
                action,
                lead,
                correlationId,
                now,
                "WorkflowStageNoLongerEligible",
                transaction,
                cancellationToken);
            return null;
        }

        customer ??= new Customer(
            Guid.CreateVersion7(),
            tenantId,
            lead.PrimaryPhoneE164,
            now,
            smsConsentBasis: "Customer-initiated workflow conversation");
        if (dbContext.Entry(customer).State == EntityState.Detached)
        {
            dbContext.Customers.Add(customer);
        }

        if (lead.CustomerId is null)
        {
            lead.AssociateCustomer(customer.Id, now);
        }

        Conversation? conversation = await dbContext.Conversations
            .Where(candidate =>
                candidate.LeadId == lead.Id &&
                candidate.Channel == ConversationChannel.Sms &&
                candidate.Status == ConversationStatus.Open)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation(
                Guid.CreateVersion7(),
                tenantId,
                lead.Id,
                ConversationChannel.Sms,
                now);
            dbContext.Conversations.Add(conversation);
        }

        string messageIdempotencyKey = $"scheduled-action:{action.Id:N}";
        Message? message = await dbContext.Messages.SingleOrDefaultAsync(
            candidate => candidate.ClientIdempotencyKey == messageIdempotencyKey,
            cancellationToken);
        if (message is null)
        {
            message = Message.QueueOutbound(
                Guid.CreateVersion7(),
                tenantId,
                lead.Id,
                conversation.Id,
                MessageKind.Automated,
                "Twilio",
                messageIdempotencyKey,
                content.Body,
                now,
                templateId: content.TemplateId);
            dbContext.Messages.Add(message);
        }

        action.Start(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PreparedOutboundSms(
            tenantId,
            action.Id,
            message.Id,
            new SmsSendRequest(
                tenantId,
                sendingNumber!.PhoneNumberE164,
                lead.PrimaryPhoneE164,
                message.Body,
                message.ClientIdempotencyKey,
                statusCallbackUri));
    }

    public async Task<OutboundSmsOutcome> CompleteWorkflowOutboundAsync(
        PreparedOutboundSms prepared,
        SmsSendResult result,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using IDisposable tenantScope = tenantExecutionScope.Begin(prepared.TenantId);
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        ScheduledAction action = await dbContext.ScheduledActions
            .FromSqlInterpolated(
                $"select * from scheduled_actions where id = {prepared.ActionId} and tenant_id = {prepared.TenantId} for update")
            .SingleAsync(cancellationToken);
        Message message = await dbContext.Messages.SingleAsync(
            candidate => candidate.Id == prepared.MessageId,
            cancellationToken);
        if (action.Status != ScheduledActionStatus.Running ||
            message.Status != MessageStatus.Queued)
        {
            await transaction.CommitAsync(cancellationToken);
            return OutboundSmsOutcome.Ignored;
        }

        Lead lead = await dbContext.Leads.SingleAsync(
            candidate => candidate.Id == action.LeadId,
            cancellationToken);
        OutboundSmsOutcome outcome;
        switch (result.Disposition)
        {
            case SmsSendDisposition.Accepted:
                message.MarkSent(
                    result.ProviderMessageSid ?? throw new InvalidOperationException(
                        "An accepted provider result requires a message SID."),
                    now);
                action.Complete(now);
                lead.RecordBusinessActivity(now);
                if (action.ActionType ==
                        WorkflowScheduledActionTypes.SendQualificationQuestion &&
                    lead.Status == LeadStatus.Contacting)
                {
                    lead.AwaitCustomer(now);
                }

                if (action.ActionType != WorkflowScheduledActionTypes.SendFollowUpSms &&
                    WorkflowScheduledActionPayloadSerializer.TryDeserialize(
                        action.PayloadJson,
                        out WorkflowScheduledActionPayload? payload))
                {
                    Tenant tenant = await dbContext.Tenants.SingleAsync(
                        candidate => candidate.Id == prepared.TenantId,
                        cancellationToken);
                    WorkflowDefinition? workflow = await dbContext.WorkflowDefinitions
                        .SingleOrDefaultAsync(candidate => candidate.IsActive, cancellationToken);
                    if (workflow is not null)
                    {
                        _ = await actionScheduler.ScheduleFollowUpsAsync(
                            tenant,
                            lead,
                            workflow,
                            payload!.Stage,
                            now,
                            cancellationToken);
                    }
                }

                outcome = OutboundSmsOutcome.Accepted;
                break;
            case SmsSendDisposition.TransientFailure:
                action.Retry(
                    now.AddMinutes(1),
                    NormalizeFailure(result, "Transient provider failure"),
                    now);
                outcome = OutboundSmsOutcome.RetryScheduled;
                break;
            case SmsSendDisposition.PermanentFailure:
                message.MarkFailed(result.FailureCode, result.FailureDescription);
                action.Fail(NormalizeFailure(result, "Permanent provider failure"), now);
                outcome = OutboundSmsOutcome.PermanentlyFailed;
                break;
            default:
                throw new InvalidOperationException("Unknown SMS provider disposition.");
        }

        AddAudit(
            lead,
            "WorkflowSmsProcessed",
            correlationId,
            now,
            new
            {
                actionId = action.Id,
                actionType = action.ActionType,
                result = outcome.ToString(),
                failureCode = result.FailureCode,
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private async Task<ResolvedContent?> ResolveContent(
        ScheduledAction action,
        WorkflowScheduledActionPayload payload,
        WorkflowDefinition workflow,
        Lead lead,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        if (action.ActionType == WorkflowScheduledActionTypes.SendQualificationQuestion)
        {
            if (lead.Status is not (LeadStatus.Contacting or LeadStatus.AwaitingCustomer) ||
                string.IsNullOrWhiteSpace(payload.QuestionKey) ||
                await dbContext.QualificationAnswers.AnyAsync(
                    answer =>
                        answer.LeadId == lead.Id &&
                        answer.QuestionKey == payload.QuestionKey,
                    cancellationToken))
            {
                return null;
            }

            QualificationQuestionPolicy? question = workflow
                .GetQualificationQuestions()
                .SingleOrDefault(candidate => candidate.Key.Equals(
                    payload.QuestionKey,
                    StringComparison.OrdinalIgnoreCase));
            return question is null ? null : new ResolvedContent(question.Prompt, null);
        }

        if (action.ActionType == WorkflowScheduledActionTypes.SendBookingLink)
        {
            if (lead.Status != LeadStatus.BookingOffered)
            {
                return null;
            }
        }
        else if (action.ActionType == WorkflowScheduledActionTypes.SendFollowUpSms)
        {
            FollowUpStepPolicy? step = workflow.GetFollowUpSteps().SingleOrDefault(
                candidate => candidate.Sequence == payload.FollowUpSequence &&
                    candidate.TemplatePurpose.Equals(
                        payload.TemplatePurpose,
                        StringComparison.OrdinalIgnoreCase));
            if (step is null || payload.BaselineCustomerActivityAtUtc !=
                lead.LastCustomerActivityAtUtc)
            {
                return null;
            }

            string[] purposes = workflow.GetFollowUpSteps()
                .Select(candidate => candidate.TemplatePurpose)
                .ToArray();
            int sentCount = await (
                from message in dbContext.Messages
                join template in dbContext.MessageTemplates
                    on message.TemplateId equals template.Id
                where message.LeadId == lead.Id &&
                    purposes.Contains(template.Purpose) &&
                    message.Status != MessageStatus.Failed &&
                    message.Status != MessageStatus.Suppressed
                select message.Id).CountAsync(cancellationToken);
            if (sentCount >= workflow.GetFollowUpSteps().Length)
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(payload.TemplatePurpose))
        {
            return null;
        }

        MessageTemplate? activeTemplate = await dbContext.MessageTemplates
            .SingleOrDefaultAsync(
                template =>
                    template.Purpose == payload.TemplatePurpose &&
                    template.IsApproved &&
                    template.IsActive,
                cancellationToken);
        if (activeTemplate is null)
        {
            return null;
        }

        string body = activeTemplate.Body
            .Replace("{{BusinessName}}", tenant.Name, StringComparison.Ordinal)
            .Replace("{{BookingUrl}}", workflow.BookingUrl, StringComparison.Ordinal);
        return new ResolvedContent(body, activeTemplate.Id);
    }

    private async Task Cancel(
        ScheduledAction action,
        Lead lead,
        string correlationId,
        DateTimeOffset now,
        string reason,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        action.Cancel(now);
        AddAudit(
            lead,
            "WorkflowSmsSuppressed",
            correlationId,
            now,
            new { actionId = action.Id, actionType = action.ActionType, reason });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void AddAudit(
        Lead lead,
        string action,
        string correlationId,
        DateTimeOffset now,
        object result) =>
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.CreateVersion7(),
            lead.TenantId,
            "System",
            "WorkflowWorker",
            action,
            nameof(Lead),
            lead.Id.ToString("N"),
            correlationId,
            now,
            afterJson: JsonSerializer.Serialize(result)));

    private static string NormalizeFailure(SmsSendResult result, string fallback)
    {
        string value = string.Join(
            ": ",
            new[] { result.FailureCode, result.FailureDescription }
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate)));
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private sealed record ResolvedContent(string Body, Guid? TemplateId);
}
