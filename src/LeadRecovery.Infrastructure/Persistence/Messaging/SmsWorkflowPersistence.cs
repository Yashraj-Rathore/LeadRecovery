using System.Data;
using System.Text.Json;

using LeadRecovery.Application.Analysis;
using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Observability;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeadRecovery.Infrastructure.Persistence.Messaging;

internal sealed class SmsWorkflowPersistence(
    LeadRecoveryDbContext dbContext,
    ITenantExecutionScope tenantExecutionScope,
    IWorkflowActionScheduler workflowActionScheduler,
    IQualificationEvaluator qualificationEvaluator,
    IBusinessHoursScheduler businessHoursScheduler,
    IAutomationRuntimePolicy automationRuntimePolicy,
    LeadAnalysisWorkflowOptions analysisOptions)
    : ISmsWorkflowPersistence
{
    private static readonly HashSet<string> OptOutKeywords = new(
        ["STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT"],
        StringComparer.Ordinal);

    public async Task<PreparedOutboundSms?> PrepareOutboundAsync(
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
            action.ActionType != ProcessCallStatusWebhookUseCase.RecoveryActionType ||
            action.Status != ScheduledActionStatus.Pending ||
            action.ScheduledForUtc > now)
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
        TenantPhoneNumber? sendingNumber = await dbContext.TenantPhoneNumbers
            .Where(number => number.MissedCallRecoveryEnabled)
            .OrderByDescending(number => number.IsPrimary)
            .ThenBy(number => number.Id)
            .FirstOrDefaultAsync(cancellationToken);
        Customer? customer = await dbContext.Customers.SingleOrDefaultAsync(
            candidate => candidate.PhoneE164 == lead.PrimaryPhoneE164,
            cancellationToken);

        bool tenantOperational =
            tenant.Status is TenantStatus.Trial or TenantStatus.Active &&
            tenant.AutomationEnabled &&
            automationRuntimePolicy.GlobalAutomationEnabled;
        bool leadEligible =
            lead.AutomationState == AutomationState.Active &&
            lead.Status is not (LeadStatus.Booked or LeadStatus.Closed or LeadStatus.ClosedWon);
        if (!tenantOperational || !leadEligible || sendingNumber is null ||
            customer?.OptedOutAtUtc is not null)
        {
            action.Cancel(now);
            AddAudit(
                tenantId,
                "RecoverySmsSuppressed",
                nameof(ScheduledAction),
                action.Id,
                correlationId,
                now,
                new { result = "ExecutionTimeEligibilityFailed" });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        MessageTemplate? template = await dbContext.MessageTemplates.SingleOrDefaultAsync(
            candidate => candidate.Purpose == SmsTemplatePurposes.InitialMissedCallRecovery &&
                candidate.IsApproved && candidate.IsActive,
            cancellationToken);
        if (template is null)
        {
            action.Start(now);
            action.Fail("No approved active recovery template is configured.", now);
            AddAudit(
                tenantId,
                "RecoverySmsFailed",
                nameof(ScheduledAction),
                action.Id,
                correlationId,
                now,
                new { result = "TemplateUnavailable" });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        customer ??= new Customer(
            Guid.CreateVersion7(),
            tenantId,
            lead.PrimaryPhoneE164,
            now,
            smsConsentBasis: "Existing business relationship - inbound call");
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
            string body = template.Body.Replace(
                "{{BusinessName}}",
                tenant.Name,
                StringComparison.Ordinal);
            message = Message.QueueOutbound(
                Guid.CreateVersion7(),
                tenantId,
                lead.Id,
                conversation.Id,
                MessageKind.Automated,
                "Twilio",
                messageIdempotencyKey,
                body,
                now,
                templateId: template.Id);
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
                sendingNumber.PhoneNumberE164,
                lead.PrimaryPhoneE164,
                message.Body,
                message.ClientIdempotencyKey,
                statusCallbackUri));
    }

    public async Task<OutboundSmsOutcome> CompleteOutboundAsync(
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

        OutboundSmsOutcome outcome;
        switch (result.Disposition)
        {
            case SmsSendDisposition.Accepted:
                message.MarkSent(
                    result.ProviderMessageSid ?? throw new InvalidOperationException(
                        "An accepted provider result requires a message SID."),
                    now);
                action.Complete(now);
                Lead lead = await dbContext.Leads.SingleAsync(
                    candidate => candidate.Id == action.LeadId,
                    cancellationToken);
                lead.RecordBusinessActivity(now);
                if (lead.Status == LeadStatus.New)
                {
                    lead.BeginContacting(now);
                }

                Tenant tenant = await dbContext.Tenants.SingleAsync(
                    candidate => candidate.Id == prepared.TenantId,
                    cancellationToken);
                WorkflowDefinition? workflow = await dbContext.WorkflowDefinitions
                    .SingleOrDefaultAsync(candidate => candidate.IsActive, cancellationToken);
                if (workflow is not null)
                {
                    _ = await workflowActionScheduler.ScheduleFirstQualificationAsync(
                        tenant,
                        lead,
                        workflow,
                        now,
                        cancellationToken);
                }

                outcome = OutboundSmsOutcome.Accepted;
                break;
            case SmsSendDisposition.TransientFailure:
                action.Retry(
                    now,
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
            prepared.TenantId,
            "RecoverySmsProcessed",
            nameof(Message),
            message.Id,
            correlationId,
            now,
            new { result = outcome.ToString(), failureCode = result.FailureCode });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    public async Task<InboundSmsOutcome> ProcessInboundAsync(
        InboundSmsWebhookEvent webhookEvent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var route = await (
            from number in dbContext.TenantPhoneNumbers.IgnoreQueryFilters()
            join tenant in dbContext.Tenants on number.TenantId equals tenant.Id
            where number.Provider == webhookEvent.Provider &&
                number.PhoneNumberE164 == webhookEvent.ToPhoneE164
            select new
            {
                number.TenantId,
                number.InboundSmsEnabled,
                tenant.Status,
            }).SingleOrDefaultAsync(cancellationToken);

        IDisposable? tenantScope = route is null
            ? null
            : tenantExecutionScope.Begin(route.TenantId);
        try
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            ExternalEventReceipt receipt = new(
                Guid.CreateVersion7(),
                route?.TenantId,
                webhookEvent.Provider,
                "InboundSms",
                webhookEvent.ExternalEventId,
                webhookEvent.PayloadHash,
                now);
            if (!await TryAddReceiptAsync(receipt, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return InboundSmsOutcome.Duplicate;
            }

            if (route is null)
            {
                receipt.MarkProcessed(InboundSmsOutcome.IgnoredUnknownNumber.ToString(), now);
                AddAudit(
                    null,
                    "InboundSmsIgnored",
                    nameof(ExternalEventReceipt),
                    receipt.Id,
                    webhookEvent.CorrelationId,
                    now,
                    new { result = InboundSmsOutcome.IgnoredUnknownNumber.ToString() });
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return InboundSmsOutcome.IgnoredUnknownNumber;
            }

            if (!route.InboundSmsEnabled ||
                route.Status is not (TenantStatus.Trial or TenantStatus.Active))
            {
                receipt.MarkProcessed(InboundSmsOutcome.IgnoredTenantInactive.ToString(), now);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return InboundSmsOutcome.IgnoredTenantInactive;
            }

            Customer? customer = await dbContext.Customers.SingleOrDefaultAsync(
                candidate => candidate.PhoneE164 == webhookEvent.FromPhoneE164,
                cancellationToken);
            if (customer is null)
            {
                customer = new Customer(
                    Guid.CreateVersion7(),
                    route.TenantId,
                    webhookEvent.FromPhoneE164,
                    now,
                    smsConsentBasis: "Customer-initiated inbound SMS");
                dbContext.Customers.Add(customer);
            }

            Lead? lead = await dbContext.Leads
                .Where(candidate => candidate.PrimaryPhoneE164 == webhookEvent.FromPhoneE164)
                .OrderByDescending(candidate => candidate.UpdatedAtUtc)
                .ThenByDescending(candidate => candidate.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (lead is null ||
                lead.Status is LeadStatus.Booked or LeadStatus.Closed or LeadStatus.ClosedWon)
            {
                lead = new Lead(
                    Guid.CreateVersion7(),
                    route.TenantId,
                    webhookEvent.FromPhoneE164,
                    LeadSource.InboundSms,
                    now);
                dbContext.Leads.Add(lead);
            }

            if (lead.CustomerId is null)
            {
                lead.AssociateCustomer(customer.Id, now);
            }

            lead.RecordCustomerActivity(now);
            if (lead.Status == LeadStatus.New)
            {
                lead.BeginContacting(now);
                lead.AwaitCustomer(now);
            }
            else if (lead.Status == LeadStatus.Contacting)
            {
                lead.AwaitCustomer(now);
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
                    route.TenantId,
                    lead.Id,
                    ConversationChannel.Sms,
                    now);
                dbContext.Conversations.Add(conversation);
            }

            Message message = Message.ReceiveInbound(
                Guid.CreateVersion7(),
                route.TenantId,
                lead.Id,
                conversation.Id,
                MessageKind.Manual,
                webhookEvent.Provider,
                webhookEvent.ProviderMessageSid,
                $"twilio-inbound:{webhookEvent.ProviderMessageSid}",
                webhookEvent.Body,
                now);
            dbContext.Messages.Add(message);

            bool optedOut = OptOutKeywords.Contains(webhookEvent.Body.Trim().ToUpperInvariant());
            string? qualificationQuestionKey = null;
            QualificationAnswerOutcome? qualificationOutcome = null;
            DateTimeOffset? humanReviewAtUtc = null;
            Guid? analysisActionId = null;
            if (optedOut)
            {
                customer.OptOut(now);
                lead.SuppressForOptOut(now);
                List<ScheduledAction> pendingActions = await dbContext.ScheduledActions
                    .Where(action =>
                        action.LeadId == lead.Id &&
                        action.Status == ScheduledActionStatus.Pending)
                    .ToListAsync(cancellationToken);
                foreach (ScheduledAction pendingAction in pendingActions)
                {
                    pendingAction.Cancel(now);
                }
            }
            else
            {
                _ = await workflowActionScheduler.CancelPendingFollowUpsAsync(
                    lead.Id,
                    now,
                    cancellationToken);
                WorkflowDefinition? workflow = await dbContext.WorkflowDefinitions
                    .SingleOrDefaultAsync(candidate => candidate.IsActive, cancellationToken);
                if (workflow is not null)
                {
                    Tenant tenant = await dbContext.Tenants.SingleAsync(
                        candidate => candidate.Id == route.TenantId,
                        cancellationToken);
                    QualificationQuestionPolicy? currentQuestion =
                        await GetCurrentQualificationQuestion(
                            lead.Id,
                            workflow,
                            cancellationToken);
                    if (currentQuestion is null)
                    {
                        _ = await workflowActionScheduler.ScheduleFirstQualificationAsync(
                            tenant,
                            lead,
                            workflow,
                            now,
                            cancellationToken);
                    }
                    else
                    {
                        QualificationEvaluation evaluation = qualificationEvaluator.Evaluate(
                            currentQuestion,
                            webhookEvent.Body);
                        qualificationQuestionKey = currentQuestion.Key;
                        qualificationOutcome = evaluation.Outcome;
                        dbContext.QualificationAnswers.Add(new QualificationAnswer(
                            Guid.CreateVersion7(),
                            route.TenantId,
                            lead.Id,
                            message.Id,
                            currentQuestion.Key,
                            evaluation.Value,
                            evaluation.Outcome,
                            now));
                        if (evaluation.Outcome != QualificationAnswerOutcome.Accepted)
                        {
                            if (lead.Status != LeadStatus.NeedsHuman)
                            {
                                lead.RequireHumanReview(now);
                            }

                            lead.ChangeUrgency(LeadUrgency.CriticalReview, now);
                            humanReviewAtUtc =
                                businessHoursScheduler.GetUrgentHumanReviewUtc(
                                    now,
                                    tenant.TimezoneId,
                                    workflow.GetBusinessHoursPolicy());
                            await CancelPendingAutomatedActions(
                                lead.Id,
                                now,
                                cancellationToken);
                        }
                        else if (lead.Status != LeadStatus.NeedsHuman)
                        {
                            QualificationQuestionPolicy[] questions =
                                workflow.GetQualificationQuestions();
                            int currentIndex = Array.FindIndex(
                                questions,
                                question => question.Key.Equals(
                                    currentQuestion.Key,
                                    StringComparison.OrdinalIgnoreCase));
                            if (currentIndex + 1 < questions.Length)
                            {
                                _ = await workflowActionScheduler
                                    .ScheduleQualificationQuestionAsync(
                                        tenant,
                                        lead,
                                        workflow,
                                        questions[currentIndex + 1].Key,
                                        now,
                                        cancellationToken);
                            }
                            else
                            {
                                lead.Qualify(true, null, now);
                                lead.OfferBooking(now);
                                _ = await workflowActionScheduler.ScheduleBookingLinkAsync(
                                    tenant,
                                    lead,
                                    workflow,
                                    LeadStatus.BookingOffered.ToString(),
                                    now,
                                    cancellationToken);
                            }
                        }
                    }

                    analysisActionId = await ScheduleLeadAnalysisAsync(
                        tenant,
                        lead,
                        workflow,
                        message,
                        webhookEvent.CorrelationId,
                        now,
                        cancellationToken);
                }
            }

            InboundSmsOutcome outcome = optedOut
                ? InboundSmsOutcome.OptedOut
                : InboundSmsOutcome.Received;
            receipt.MarkProcessed(outcome.ToString(), now);
            AddAudit(
                route.TenantId,
                optedOut ? "CustomerSmsOptedOut" : "InboundSmsReceived",
                nameof(Lead),
                lead.Id,
                webhookEvent.CorrelationId,
                now,
                new
                {
                    result = outcome.ToString(),
                    messageId = message.Id,
                    qualificationQuestionKey,
                    qualificationOutcome = qualificationOutcome?.ToString(),
                    humanReviewAtUtc,
                    analysisActionId,
                });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return outcome;
        }
        finally
        {
            tenantScope?.Dispose();
        }
    }

    private async Task<Guid?> ScheduleLeadAnalysisAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        Message sourceMessage,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!analysisOptions.Enabled ||
            !automationRuntimePolicy.GlobalAutomationEnabled ||
            !tenant.AutomationEnabled ||
            lead.AutomationState != AutomationState.Active ||
            lead.Status is LeadStatus.Booked or LeadStatus.Closed or LeadStatus.ClosedWon)
        {
            return null;
        }

        QualificationQuestionPolicy? categoryQuestion = workflow
            .GetQualificationQuestions()
            .SingleOrDefault(question =>
                question.AnswerKind == QualificationAnswerKind.Choice &&
                question.Key.Equals(
                    analysisOptions.CategoryQuestionKey,
                    StringComparison.OrdinalIgnoreCase));
        string[]? categories = categoryQuestion?.AllowedValues;
        if (categories is null ||
            categories.Length is 0 or > LeadAnalysisSchema.MaximumAllowedCategories ||
            categories.Any(category => category.Equals(
                LeadAnalysisSchema.UnknownCategory,
                StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        List<ScheduledAction> supersededActions = await dbContext.ScheduledActions
            .Where(action =>
                action.LeadId == lead.Id &&
                action.ActionType == LeadAnalysisScheduledActionTypes.AnalyzeLead &&
                action.Status == ScheduledActionStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (ScheduledAction supersededAction in supersededActions)
        {
            supersededAction.Cancel(now);
        }

        Guid actionId = Guid.CreateVersion7();
        LeadAnalysisScheduledActionPayload payload = new(
            1,
            LeadAnalysisSchema.CurrentVersion,
            sourceMessage.Id,
            workflow.Id,
            workflow.Version,
            categoryQuestion!.Key,
            categories.ToArray());
        WorkflowTelemetryContext telemetry = WorkflowTelemetryContextCapture.Capture(
            correlationId);
        dbContext.ScheduledActions.Add(new ScheduledAction(
            actionId,
            tenant.Id,
            lead.Id,
            LeadAnalysisScheduledActionTypes.AnalyzeLead,
            now,
            $"ai-analysis:{lead.Id:N}:{sourceMessage.Id:N}:{LeadAnalysisSchema.CurrentVersion}",
            LeadAnalysisScheduledActionPayloadSerializer.Serialize(payload),
            now,
            telemetry.CorrelationId,
            telemetry.TraceParent,
            telemetry.TraceState));
        return actionId;
    }

    private async Task<QualificationQuestionPolicy?> GetCurrentQualificationQuestion(
        Guid leadId,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        HashSet<string> answeredKeys = (await dbContext.QualificationAnswers
                .Where(answer => answer.LeadId == leadId)
                .Select(answer => answer.QuestionKey)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] payloads = await dbContext.ScheduledActions
            .Where(action =>
                action.LeadId == leadId &&
                action.ActionType ==
                    WorkflowScheduledActionTypes.SendQualificationQuestion &&
                action.Status == ScheduledActionStatus.Completed)
            .OrderByDescending(action => action.UpdatedAtUtc)
            .ThenByDescending(action => action.Id)
            .Select(action => action.PayloadJson)
            .ToArrayAsync(cancellationToken);
        QualificationQuestionPolicy[] questions = workflow.GetQualificationQuestions();
        foreach (string payloadJson in payloads)
        {
            if (!WorkflowScheduledActionPayloadSerializer.TryDeserialize(
                    payloadJson,
                    out WorkflowScheduledActionPayload? payload) ||
                string.IsNullOrWhiteSpace(payload!.QuestionKey) ||
                answeredKeys.Contains(payload.QuestionKey))
            {
                continue;
            }

            return questions.SingleOrDefault(question => question.Key.Equals(
                payload.QuestionKey,
                StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private async Task CancelPendingAutomatedActions(
        Guid leadId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<ScheduledAction> pendingActions = await dbContext.ScheduledActions
            .Where(action =>
                action.LeadId == leadId &&
                action.ActionType != SmsScheduledActionTypes.SendManualSms &&
                action.Status == ScheduledActionStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (ScheduledAction pendingAction in pendingActions)
        {
            if (pendingAction.Status == ScheduledActionStatus.Pending)
            {
                pendingAction.Cancel(now);
            }
        }
    }

    public async Task<DeliveryStatusOutcome> ProcessDeliveryStatusAsync(
        DeliveryStatusWebhookEvent webhookEvent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Message? message = await dbContext.Messages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                candidate => candidate.Provider == webhookEvent.Provider &&
                    candidate.ProviderMessageSid == webhookEvent.ProviderMessageSid,
                cancellationToken);
        IDisposable? tenantScope = message is null
            ? null
            : tenantExecutionScope.Begin(message.TenantId);
        try
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            ExternalEventReceipt receipt = new(
                Guid.CreateVersion7(),
                message?.TenantId,
                webhookEvent.Provider,
                "MessageStatus",
                webhookEvent.ExternalEventId,
                webhookEvent.PayloadHash,
                now);
            if (!await TryAddReceiptAsync(receipt, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeliveryStatusOutcome.Duplicate;
            }

            if (message is null)
            {
                receipt.MarkProcessed(DeliveryStatusOutcome.IgnoredUnknownMessage.ToString(), now);
                AddAudit(
                    null,
                    "MessageStatusIgnored",
                    nameof(ExternalEventReceipt),
                    receipt.Id,
                    webhookEvent.CorrelationId,
                    now,
                    new { result = DeliveryStatusOutcome.IgnoredUnknownMessage.ToString() });
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return DeliveryStatusOutcome.IgnoredUnknownMessage;
            }

            string status = webhookEvent.Status.ToLowerInvariant();
            DeliveryStatusOutcome outcome = DeliveryStatusOutcome.Updated;
            if (status == "sent" && message.Status == MessageStatus.Queued)
            {
                message.MarkSent(webhookEvent.ProviderMessageSid, now);
            }
            else if (status == "delivered" &&
                message.Status is MessageStatus.Queued or MessageStatus.Sent)
            {
                if (message.Status == MessageStatus.Queued)
                {
                    message.MarkSent(webhookEvent.ProviderMessageSid, now);
                }

                message.MarkDelivered(now);
            }
            else if (status is "failed" or "undelivered" &&
                message.Status is MessageStatus.Queued or MessageStatus.Sent)
            {
                message.MarkFailed(
                    webhookEvent.ErrorCode ?? status,
                    "The provider reported a permanent delivery failure.");
            }
            else
            {
                outcome = DeliveryStatusOutcome.IgnoredStatus;
            }

            receipt.MarkProcessed(outcome.ToString(), now);
            AddAudit(
                message.TenantId,
                "MessageStatusUpdated",
                nameof(Message),
                message.Id,
                webhookEvent.CorrelationId,
                now,
                new
                {
                    result = outcome.ToString(),
                    providerStatus = status,
                    errorCode = webhookEvent.ErrorCode,
                });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return outcome;
        }
        finally
        {
            tenantScope?.Dispose();
        }
    }

    private async Task<bool> TryAddReceiptAsync(
        ExternalEventReceipt receipt,
        CancellationToken cancellationToken)
    {
        int inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into external_event_receipts
                (id, tenant_id, provider, event_type, external_event_id,
                 payload_hash, received_at_utc)
            values
                ({receipt.Id}, {receipt.TenantId}, {receipt.Provider},
                 {receipt.EventType}, {receipt.ExternalEventId},
                 {receipt.PayloadHash}, {receipt.ReceivedAtUtc})
            on conflict (provider, event_type, external_event_id) do nothing
            """,
            cancellationToken);
        if (inserted == 0)
        {
            return false;
        }

        dbContext.Attach(receipt);
        return true;
    }

    private void AddAudit(
        Guid? tenantId,
        string action,
        string entityType,
        Guid entityId,
        string correlationId,
        DateTimeOffset now,
        object result) =>
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.CreateVersion7(),
            tenantId,
            "Integration",
            "Twilio",
            action,
            entityType,
            entityId.ToString("N"),
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
}
