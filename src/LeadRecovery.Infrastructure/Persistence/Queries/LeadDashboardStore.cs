using System.Data;
using System.Text.Json;

using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Leads;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Analysis;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeadRecovery.Infrastructure.Persistence.Queries;

internal sealed class LeadDashboardStore(
    LeadRecoveryDbContext dbContext,
    ITenantContext tenantContext,
    IWorkflowActionScheduler workflowActionScheduler)
    : ILeadDashboardStore
{
    public async Task<LeadDetail?> GetDetailAsync(
        Guid leadId,
        CancellationToken cancellationToken)
    {
        LeadInboxItem? lead = await dbContext.Leads
            .AsNoTracking()
            .Where(candidate => candidate.Id == leadId)
            .Select(candidate => new LeadInboxItem(
                candidate.Id,
                candidate.DisplayName,
                candidate.PrimaryPhoneE164,
                candidate.Source,
                candidate.Status,
                candidate.Urgency,
                candidate.AutomationState,
                candidate.AssignedUserId,
                candidate.AssignedUserId == null
                    ? null
                    : dbContext.Users
                        .Where(user => user.Id == candidate.AssignedUserId)
                        .Select(user => user.DisplayName)
                        .SingleOrDefault(),
                candidate.LastCustomerActivityAtUtc ??
                    candidate.LastBusinessActivityAtUtc ??
                    candidate.CreatedAtUtc,
                candidate.LastCustomerActivityAtUtc != null &&
                    (candidate.LastBusinessActivityAtUtc == null ||
                        candidate.LastCustomerActivityAtUtc >
                        candidate.LastBusinessActivityAtUtc),
                candidate.Version,
                candidate.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var messages = await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.LeadId == leadId)
            .Select(message => new
            {
                message.Id,
                message.Direction,
                message.Kind,
                message.Body,
                message.Status,
                message.FailureDescription,
                message.SentByUserId,
                ActorName = message.SentByUserId == null
                    ? null
                    : dbContext.Users
                        .Where(user => user.Id == message.SentByUserId)
                        .Select(user => user.DisplayName)
                        .SingleOrDefault(),
                message.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var notes = await dbContext.LeadNotes
            .AsNoTracking()
            .Where(note => note.LeadId == leadId)
            .Select(note => new
            {
                note.Id,
                note.Body,
                note.CreatedAtUtc,
                ActorName = dbContext.Users
                    .Where(user => user.Id == note.AuthorUserId)
                    .Select(user => user.DisplayName)
                    .SingleOrDefault(),
            })
            .ToListAsync(cancellationToken);

        AiAnalysis[] analyses = await dbContext.AiAnalyses
            .AsNoTracking()
            .Where(analysis => analysis.LeadId == leadId)
            .OrderByDescending(analysis => analysis.CreatedAtUtc)
            .ThenByDescending(analysis => analysis.Id)
            .ToArrayAsync(cancellationToken);
        string[] analysisEntityIds = analyses
            .Select(analysis => analysis.Id.ToString("N"))
            .ToArray();
        Guid[] reviewerIds = analyses
            .Where(analysis => analysis.ReviewedByUserId != null)
            .Select(analysis => analysis.ReviewedByUserId!.Value)
            .Distinct()
            .ToArray();
        Dictionary<Guid, string> reviewerNames = await dbContext.Users
            .AsNoTracking()
            .Where(user => reviewerIds.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => user.DisplayName,
                cancellationToken);

        string entityId = leadId.ToString("N");
        var audits = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(auditEvent =>
                auditEvent.TenantId == tenantContext.TenantId &&
                ((auditEvent.EntityType == nameof(Lead) &&
                    auditEvent.EntityId == entityId) ||
                    (auditEvent.EntityType == nameof(AiAnalysis) &&
                    analysisEntityIds.Contains(auditEvent.EntityId))))
            .Select(auditEvent => new
            {
                auditEvent.Id,
                auditEvent.Action,
                auditEvent.ActorType,
                auditEvent.ActorId,
                auditEvent.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        List<LeadTimelineItem> timeline = messages
            .Select(message => new LeadTimelineItem(
                message.Id,
                "Sms",
                message.Kind == MessageKind.Manual
                    ? "Manual SMS"
                    : message.Direction == MessageDirection.Inbound
                        ? "Customer SMS"
                        : "Automated SMS",
                message.Body,
                message.Direction.ToString(),
                message.Kind.ToString(),
                message.Status.ToString(),
                message.FailureDescription,
                message.ActorName,
                message.CreatedAtUtc))
            .Concat(notes.Select(note => new LeadTimelineItem(
                note.Id,
                "Note",
                "Internal note",
                note.Body,
                null,
                null,
                null,
                null,
                note.ActorName,
                note.CreatedAtUtc)))
            .Concat(audits.Select(audit => new LeadTimelineItem(
                audit.Id,
                audit.Action == "MissedCallRecoveryScheduled" ? "Call" : "System",
                GetAuditLabel(audit.Action),
                null,
                null,
                null,
                null,
                null,
                audit.ActorType == "User" ? "Staff user" : audit.ActorId,
                audit.CreatedAtUtc)))
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Type, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .ToList();

        PendingActionItem[] pendingActions = await dbContext.ScheduledActions
            .AsNoTracking()
            .Where(action =>
                action.LeadId == leadId &&
                (action.Status == ScheduledActionStatus.Pending ||
                    action.Status == ScheduledActionStatus.Running))
            .OrderBy(action => action.ScheduledForUtc)
            .ThenBy(action => action.Id)
            .Select(action => new PendingActionItem(
                action.Id,
                action.ActionType,
                action.Status,
                action.ScheduledForUtc,
                action.AttemptCount,
                action.Status == ScheduledActionStatus.Pending))
            .ToArrayAsync(cancellationToken);

        WorkflowDefinition? workflow = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IsActive, cancellationToken);
        var persistedAnswers = await dbContext.QualificationAnswers
            .AsNoTracking()
            .Where(answer => answer.LeadId == leadId)
            .OrderBy(answer => answer.CreatedAtUtc)
            .ThenBy(answer => answer.Id)
            .Select(answer => new
            {
                answer.Id,
                answer.QuestionKey,
                answer.Value,
                answer.Outcome,
                answer.CreatedAtUtc,
            })
            .ToArrayAsync(cancellationToken);
        Dictionary<string, QualificationQuestionPolicy> questionByKey = workflow?
            .GetQualificationQuestions()
            .ToDictionary(question => question.Key, StringComparer.OrdinalIgnoreCase) ?? [];
        QualificationAnswerItem[] qualificationAnswers = persistedAnswers
            .Select(answer => new QualificationAnswerItem(
                answer.Id,
                answer.QuestionKey,
                questionByKey.TryGetValue(answer.QuestionKey, out QualificationQuestionPolicy? question)
                    ? question.Prompt
                    : answer.QuestionKey,
                answer.Value,
                answer.Outcome.ToString(),
                answer.CreatedAtUtc))
            .ToArray();
        HashSet<string> answeredKeys = persistedAnswers
            .Select(answer => answer.QuestionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? currentQualificationQuestion = workflow?
            .GetQualificationQuestions()
            .FirstOrDefault(question => !answeredKeys.Contains(question.Key))?
            .Prompt;
        string? bookingUrl = workflow is not null &&
            lead.Status is LeadStatus.Qualified or LeadStatus.BookingOffered
                ? workflow.BookingUrl
                : null;

        IReadOnlyList<AssignableUserItem> users =
            await ListAssignableUsersAsync(cancellationToken);
        LeadStatus[] allowedTransitions = Enum.GetValues<LeadStatus>()
            .Where(target => LeadStatusTransitionPolicy.CanTransition(lead.Status, target))
            .ToArray();
        AiAnalysisReviewItem[] analysisItems = analyses
            .Select(analysis => new AiAnalysisReviewItem(
                analysis.Id,
                analysis.SchemaVersion,
                analysis.GetAllowedCategories(),
                analysis.GetSuggestion(),
                analysis.Confidence,
                analysis.RequiresHumanReview,
                analysis.GetReasonCodes(),
                analysis.ReviewStatus,
                analysis.GetReviewedValues(),
                analysis.CorrectionReason,
                analysis.ReviewedByUserId,
                analysis.ReviewedByUserId is Guid reviewerId &&
                    reviewerNames.TryGetValue(reviewerId, out string? reviewerName)
                        ? reviewerName
                        : null,
                analysis.ReviewedAtUtc,
                analysis.Version,
                analysis.CreatedAtUtc))
            .ToArray();
        return new LeadDetail(
            lead,
            timeline,
            pendingActions,
            users,
            allowedTransitions,
            qualificationAnswers,
            currentQualificationQuestion,
            bookingUrl,
            analysisItems);
    }

    public async Task<IReadOnlyList<AssignableUserItem>> ListAssignableUsersAsync(
        CancellationToken cancellationToken) =>
        await (
            from membership in dbContext.TenantMemberships.AsNoTracking()
            join user in dbContext.Users.AsNoTracking()
                on membership.UserId equals user.Id
            where user.IsActive
            orderby user.DisplayName, user.Id
            select new AssignableUserItem(
                user.Id,
                user.DisplayName,
                membership.Role.ToString()))
            .ToArrayAsync(cancellationToken);

    public async Task<LeadOperationResult> AssignAsync(
        Guid leadId,
        Guid? assignedUserId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Begin(cancellationToken);
        Lead? lead = await dbContext.Leads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        if (lead.Version != expectedVersion)
        {
            return await Rollback(transaction, LeadOperationResult.Conflict(), cancellationToken);
        }

        if (assignedUserId is Guid userId)
        {
            bool canAssign = await (
                from membership in dbContext.TenantMemberships
                join user in dbContext.Users on membership.UserId equals user.Id
                where membership.UserId == userId && user.IsActive
                select membership.Id).AnyAsync(cancellationToken);
            if (!canAssign)
            {
                return await Rollback(
                    transaction,
                    LeadOperationResult.Invalid(
                        "The selected user is not an active member of this tenant."),
                    cancellationToken);
            }
        }

        Guid? before = lead.AssignedUserId;
        if (assignedUserId is Guid targetUserId)
        {
            lead.AssignTo(targetUserId, now);
        }
        else
        {
            lead.Unassign(now);
        }

        AddAudit(
            lead,
            actorUserId,
            "LeadAssigned",
            correlationId,
            now,
            new { assignedUserId = before },
            new { assignedUserId });
        return await Save(transaction, cancellationToken);
    }

    public async Task<LeadOperationResult> TransitionAsync(
        Guid leadId,
        LeadTransitionCommand command,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Begin(cancellationToken);
        Lead? lead = await dbContext.Leads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        if (lead.Version != expectedVersion)
        {
            return await Rollback(transaction, LeadOperationResult.Conflict(), cancellationToken);
        }

        LeadStatus before = lead.Status;
        try
        {
            ApplyTransition(lead, command, now);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid(exception.Message),
                cancellationToken);
        }

        if (command.TargetStatus is LeadStatus.Booked or LeadStatus.Closed)
        {
            await CancelPendingAutomations(lead.Id, now, cancellationToken);
        }

        AddAudit(
            lead,
            actorUserId,
            "LeadStatusChanged",
            correlationId,
            now,
            new { status = before.ToString() },
            new
            {
                status = lead.Status.ToString(),
                reason = NormalizeOptional(command.Reason),
                closeReason = command.CloseReason?.ToString(),
            });
        return await Save(transaction, cancellationToken);
    }

    public async Task<LeadOperationResult> SetAutomationPausedAsync(
        Guid leadId,
        bool paused,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Begin(cancellationToken);
        Lead? lead = await dbContext.Leads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        if (lead.Version != expectedVersion)
        {
            return await Rollback(transaction, LeadOperationResult.Conflict(), cancellationToken);
        }

        int affectedActionCount = 0;
        try
        {
            if (paused)
            {
                lead.PauseAutomation(now);
                affectedActionCount = await CancelPendingAutomations(
                    lead.Id,
                    now,
                    cancellationToken);
            }
            else
            {
                Tenant tenant = await dbContext.Tenants.SingleAsync(
                    candidate => candidate.Id == tenantContext.TenantId,
                    cancellationToken);
                if (!tenant.AutomationEnabled ||
                    tenant.Status is not (TenantStatus.Trial or TenantStatus.Active))
                {
                    return await Rollback(
                        transaction,
                        LeadOperationResult.PolicyBlocked(
                            "Tenant automation must be enabled before a lead can resume."),
                        cancellationToken);
                }

                lead.ResumeAutomation(now);
                affectedActionCount = await CreateValidResumeAction(
                    lead,
                    expectedVersion,
                    now,
                    cancellationToken);
            }
        }
        catch (InvalidOperationException exception)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid(exception.Message),
                cancellationToken);
        }

        AddAudit(
            lead,
            actorUserId,
            paused ? "LeadAutomationPaused" : "LeadAutomationResumed",
            correlationId,
            now,
            before: null,
            after: new
            {
                automationState = lead.AutomationState.ToString(),
                affectedActionCount,
            });
        return await Save(transaction, cancellationToken);
    }

    public async Task<LeadOperationResult> AddNoteAsync(
        Guid leadId,
        string body,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Begin(cancellationToken);
        Lead? lead = await dbContext.Leads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        if (!await IsActiveMember(actorUserId, cancellationToken))
        {
            return await Rollback(
                transaction,
                LeadOperationResult.PolicyBlocked("The user is not an active tenant member."),
                cancellationToken);
        }

        LeadNote note;
        try
        {
            note = new LeadNote(
                Guid.CreateVersion7(),
                tenantContext.TenantId,
                lead.Id,
                actorUserId,
                body,
                now);
        }
        catch (ArgumentException exception)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid(exception.Message),
                cancellationToken);
        }

        dbContext.LeadNotes.Add(note);
        AddAudit(
            lead,
            actorUserId,
            "LeadNoteAdded",
            correlationId,
            now,
            before: null,
            after: new { noteId = note.Id });
        LeadOperationResult result = await Save(transaction, cancellationToken);
        return result with { ResourceId = note.Id };
    }

    public async Task<LeadOperationResult> QueueManualMessageAsync(
        Guid leadId,
        QueueManualMessageCommand command,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Begin(cancellationToken);
        Lead? lead = await dbContext.Leads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        if (!await IsActiveMember(actorUserId, cancellationToken))
        {
            return await Rollback(
                transaction,
                LeadOperationResult.PolicyBlocked("The user is not an active tenant member."),
                cancellationToken);
        }

        if (lead.AutomationState == AutomationState.SuppressedOptOut ||
            lead.Status is LeadStatus.Closed or LeadStatus.ClosedWon)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.PolicyBlocked(
                    "Messaging is blocked for an opted-out or closed lead."),
                cancellationToken);
        }

        bool optedOut = await dbContext.Customers.AnyAsync(
            customer =>
                customer.PhoneE164 == lead.PrimaryPhoneE164 &&
                customer.OptedOutAtUtc != null,
            cancellationToken);
        if (optedOut)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.PolicyBlocked(
                    "The customer opted out of SMS messaging."),
                cancellationToken);
        }

        Tenant tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == tenantContext.TenantId,
            cancellationToken);
        bool tenantOperational =
            tenant.Status is TenantStatus.Trial or TenantStatus.Active;
        bool hasSendingNumber = await dbContext.TenantPhoneNumbers.AnyAsync(
            number => number.InboundSmsEnabled,
            cancellationToken);
        if (!tenantOperational || !hasSendingNumber)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.PolicyBlocked(
                    "Manual SMS requires an active tenant and configured SMS number."),
                cancellationToken);
        }

        string idempotencyKey = command.IdempotencyKey.Trim();
        Message? existing = await dbContext.Messages.SingleOrDefaultAsync(
            message => message.ClientIdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            LeadOperationResult existingResult =
                existing.LeadId == leadId &&
                existing.Kind == MessageKind.Manual &&
                existing.Body == command.Body
                    ? LeadOperationResult.Success(existing.Id)
                    : LeadOperationResult.Invalid(
                        "The idempotency key was already used for a different message.");
            return await Rollback(transaction, existingResult, cancellationToken);
        }

        Conversation? conversation = await dbContext.Conversations
            .Where(candidate =>
                candidate.LeadId == leadId &&
                candidate.Channel == ConversationChannel.Sms &&
                candidate.Status == ConversationStatus.Open)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation(
                Guid.CreateVersion7(),
                tenantContext.TenantId,
                leadId,
                ConversationChannel.Sms,
                now);
            dbContext.Conversations.Add(conversation);
        }

        Message message;
        try
        {
            message = Message.QueueOutbound(
                Guid.CreateVersion7(),
                tenantContext.TenantId,
                leadId,
                conversation.Id,
                MessageKind.Manual,
                "Twilio",
                idempotencyKey,
                command.Body,
                now,
                sentByUserId: actorUserId);
        }
        catch (ArgumentException exception)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid(exception.Message),
                cancellationToken);
        }

        dbContext.Messages.Add(message);
        ScheduledAction action = new(
            Guid.CreateVersion7(),
            tenantContext.TenantId,
            leadId,
            SmsScheduledActionTypes.SendManualSms,
            now,
            $"manual-message:{message.Id:N}",
            JsonSerializer.Serialize(new { schemaVersion = 1, messageId = message.Id }),
            now);
        dbContext.ScheduledActions.Add(action);
        AddAudit(
            lead,
            actorUserId,
            "ManualSmsQueued",
            correlationId,
            now,
            before: null,
            after: new { messageId = message.Id, scheduledActionId = action.Id });
        LeadOperationResult result = await Save(transaction, cancellationToken);
        return result with { ResourceId = message.Id };
    }

    public async Task<LeadOperationResult> QueueBookingLinkAsync(
        Guid leadId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Begin(cancellationToken);
        Lead? lead = await dbContext.Leads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        if (lead.Version != expectedVersion)
        {
            return await Rollback(transaction, LeadOperationResult.Conflict(), cancellationToken);
        }

        if (lead.Status is not (LeadStatus.Qualified or LeadStatus.BookingOffered) ||
            lead.AutomationState != AutomationState.Active)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid(
                    "A booking link can be queued only for an active qualified lead."),
                cancellationToken);
        }

        Tenant tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == tenantContext.TenantId,
            cancellationToken);
        WorkflowDefinition? workflow = await dbContext.WorkflowDefinitions
            .SingleOrDefaultAsync(candidate => candidate.IsActive, cancellationToken);
        if (workflow is null)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.PolicyBlocked(
                    "An active tenant workflow with an approved booking URL is required."),
                cancellationToken);
        }

        LeadStatus beforeStatus = lead.Status;
        if (lead.Status == LeadStatus.Qualified)
        {
            lead.OfferBooking(now);
        }

        bool queued = await workflowActionScheduler.ScheduleBookingLinkAsync(
            tenant,
            lead,
            workflow,
            LeadStatus.BookingOffered.ToString(),
            now,
            cancellationToken);
        if (!queued)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid(
                    "The booking link is already queued or sent for this stage."),
                cancellationToken);
        }

        AddAudit(
            lead,
            actorUserId,
            "BookingLinkQueued",
            correlationId,
            now,
            before: null,
            after: new
            {
                workflowVersion = workflow.Version,
                stage = LeadStatus.BookingOffered.ToString(),
            });
        return await Save(transaction, cancellationToken);
    }

    public async Task<LeadOperationResult> CancelScheduledActionAsync(
        Guid leadId,
        Guid actionId,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Begin(cancellationToken);
        Lead? lead = await dbContext.Leads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        ScheduledAction? action = await dbContext.ScheduledActions.SingleOrDefaultAsync(
            candidate => candidate.Id == actionId && candidate.LeadId == leadId,
            cancellationToken);
        if (action is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        if (action.Status != ScheduledActionStatus.Pending)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid("Only a pending action can be cancelled."),
                cancellationToken);
        }

        ScheduledActionStatus beforeStatus = action.Status;
        action.Cancel(now);
        if (action.ActionType == SmsScheduledActionTypes.SendManualSms &&
            TryReadMessageId(action.PayloadJson, out Guid messageId))
        {
            Message? message = await dbContext.Messages.SingleOrDefaultAsync(
                candidate => candidate.Id == messageId,
                cancellationToken);
            if (message?.Status == MessageStatus.Queued)
            {
                message.Suppress();
            }
        }

        AddAudit(
            lead,
            actorUserId,
            "ScheduledActionCancelled",
            correlationId,
            now,
            before: new { status = beforeStatus.ToString(), action.ActionType },
            after: new { status = ScheduledActionStatus.Cancelled.ToString(), action.Id });
        return await Save(transaction, cancellationToken);
    }

    public async Task<LeadOperationResult> ReviewAnalysisAsync(
        Guid leadId,
        Guid analysisId,
        ReviewLeadAnalysisCommand command,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Begin(cancellationToken);
        Lead? lead = await dbContext.Leads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        AiAnalysis? analysis = await dbContext.AiAnalyses.SingleOrDefaultAsync(
            candidate => candidate.Id == analysisId && candidate.LeadId == leadId,
            cancellationToken);
        if (analysis is null)
        {
            return await Rollback(transaction, LeadOperationResult.NotFound(), cancellationToken);
        }

        if (analysis.Version != expectedVersion)
        {
            return await Rollback(transaction, LeadOperationResult.Conflict(), cancellationToken);
        }

        if (!await IsActiveMember(actorUserId, cancellationToken))
        {
            return await Rollback(
                transaction,
                LeadOperationResult.PolicyBlocked(
                    "The user is not an active tenant member."),
                cancellationToken);
        }

        string[] changedFields = [];
        try
        {
            switch (command.Action)
            {
                case LeadAnalysisReviewAction.Accept:
                    analysis.Accept(actorUserId, command.CorrectionReason, now);
                    break;
                case LeadAnalysisReviewAction.Edit:
                    AiAnalysisValues editedValues = command.EditedValues ??
                        throw new ArgumentException("Edited values are required.");
                    changedFields = GetChangedAnalysisFields(
                        analysis.GetSuggestion(),
                        editedValues);
                    analysis.Edit(
                        actorUserId,
                        editedValues,
                        command.CorrectionReason,
                        now);
                    break;
                case LeadAnalysisReviewAction.Reject:
                    analysis.Reject(actorUserId, command.CorrectionReason, now);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command));
            }
        }
        catch (ArgumentException exception)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid(exception.Message),
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await Rollback(
                transaction,
                LeadOperationResult.Invalid(exception.Message),
                cancellationToken);
        }

        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.CreateVersion7(),
            lead.TenantId,
            "User",
            actorUserId.ToString(),
            $"AiAnalysis{analysis.ReviewStatus}",
            nameof(AiAnalysis),
            analysis.Id.ToString("N"),
            correlationId,
            now,
            beforeJson: JsonSerializer.Serialize(new
            {
                reviewStatus = AiAnalysisReviewStatus.Pending.ToString(),
            }),
            afterJson: JsonSerializer.Serialize(new
            {
                leadId = lead.Id,
                reviewStatus = analysis.ReviewStatus.ToString(),
                changedFields,
                correctionReasonProvided = analysis.CorrectionReason is not null,
            })));
        return await Save(transaction, cancellationToken);
    }

    private async Task<IDbContextTransaction> Begin(CancellationToken cancellationToken) =>
        await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

    private async Task<LeadOperationResult> Save(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return LeadOperationResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return LeadOperationResult.Conflict();
        }
    }

    private static async Task<LeadOperationResult> Rollback(
        IDbContextTransaction transaction,
        LeadOperationResult result,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return result;
    }

    private async Task<bool> IsActiveMember(
        Guid userId,
        CancellationToken cancellationToken) =>
        await (
            from membership in dbContext.TenantMemberships
            join user in dbContext.Users on membership.UserId equals user.Id
            where membership.UserId == userId && user.IsActive
            select membership.Id).AnyAsync(cancellationToken);

    private async Task<int> CancelPendingAutomations(
        Guid leadId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<ScheduledAction> actions = await dbContext.ScheduledActions
            .Where(action =>
                action.LeadId == leadId &&
                action.ActionType != SmsScheduledActionTypes.SendManualSms &&
                action.Status == ScheduledActionStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (ScheduledAction action in actions)
        {
            action.Cancel(now);
        }

        return actions.Count;
    }

    private async Task<int> CreateValidResumeAction(
        Lead lead,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (lead.Source != LeadSource.MissedCall)
        {
            return 0;
        }

        bool alreadySent = await dbContext.Messages.AnyAsync(
            message =>
                message.LeadId == lead.Id &&
                message.Kind == MessageKind.Automated,
            cancellationToken);
        bool alreadyPending = await dbContext.ScheduledActions.AnyAsync(
            action =>
                action.LeadId == lead.Id &&
                action.ActionType == ProcessCallStatusWebhookUseCase.RecoveryActionType &&
                (action.Status == ScheduledActionStatus.Pending ||
                    action.Status == ScheduledActionStatus.Running),
            cancellationToken);
        if (alreadySent || alreadyPending)
        {
            return 0;
        }

        TenantPhoneNumber? number = await dbContext.TenantPhoneNumbers
            .Where(candidate => candidate.MissedCallRecoveryEnabled)
            .OrderByDescending(candidate => candidate.IsPrimary)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (number is null)
        {
            return 0;
        }

        dbContext.ScheduledActions.Add(new ScheduledAction(
            Guid.CreateVersion7(),
            lead.TenantId,
            lead.Id,
            ProcessCallStatusWebhookUseCase.RecoveryActionType,
            now.AddSeconds(number.InitialDelaySeconds),
            $"dashboard-resume:{lead.Id:N}:{expectedVersion}",
            JsonSerializer.Serialize(new { schemaVersion = 1, reason = "StaffResume" }),
            now));
        return 1;
    }

    private void AddAudit(
        Lead lead,
        Guid actorUserId,
        string action,
        string correlationId,
        DateTimeOffset now,
        object? before,
        object? after) =>
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.CreateVersion7(),
            lead.TenantId,
            "User",
            actorUserId.ToString(),
            action,
            nameof(Lead),
            lead.Id.ToString("N"),
            correlationId,
            now,
            before is null ? null : JsonSerializer.Serialize(before),
            after is null ? null : JsonSerializer.Serialize(after)));

    private static void ApplyTransition(
        Lead lead,
        LeadTransitionCommand command,
        DateTimeOffset now)
    {
        switch (command.TargetStatus)
        {
            case LeadStatus.Contacting:
                lead.BeginContacting(now);
                break;
            case LeadStatus.AwaitingCustomer:
                lead.AwaitCustomer(now);
                break;
            case LeadStatus.Qualified:
                lead.Qualify(
                    command.MinimumRequiredDetailsPresent,
                    command.Reason,
                    now);
                break;
            case LeadStatus.BookingOffered:
                lead.OfferBooking(now);
                break;
            case LeadStatus.NeedsHuman:
                lead.RequireHumanReview(now);
                break;
            case LeadStatus.Booked:
                lead.Book(now);
                break;
            case LeadStatus.Closed:
                lead.Close(
                    command.CloseReason ?? throw new ArgumentException(
                        "A close reason is required when closing a lead."),
                    now);
                break;
            case LeadStatus.ClosedWon:
                lead.ConfirmWon(now);
                break;
            default:
                throw new InvalidOperationException(
                    $"A transition to {command.TargetStatus} is not supported.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryReadMessageId(string payloadJson, out Guid messageId)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            messageId = default;
            return document.RootElement.TryGetProperty("messageId", out JsonElement value) &&
                value.TryGetGuid(out messageId) &&
                messageId != Guid.Empty;
        }
        catch (JsonException)
        {
            messageId = Guid.Empty;
            return false;
        }
    }

    private static string[] GetChangedAnalysisFields(
        AiAnalysisValues original,
        AiAnalysisValues edited)
    {
        List<string> changed = [];
        AddIfChanged(changed, "serviceCategory", original.ServiceCategory, edited.ServiceCategory);
        AddIfChanged(changed, "urgency", original.Urgency, edited.Urgency);
        AddIfChanged(changed, "summary", original.Summary, edited.Summary);
        AddIfChanged(changed, "city", original.City, edited.City);
        AddIfChanged(changed, "postalCode", original.PostalCode, edited.PostalCode);
        AddIfChanged(
            changed,
            "preferredCallbackWindow",
            original.PreferredCallbackWindow,
            edited.PreferredCallbackWindow);
        AddIfChanged(
            changed,
            "suggestedReply",
            original.SuggestedReply,
            edited.SuggestedReply);
        return changed.ToArray();
    }

    private static void AddIfChanged<T>(
        List<string> changed,
        string field,
        T original,
        T edited)
    {
        if (!EqualityComparer<T>.Default.Equals(original, edited))
        {
            changed.Add(field);
        }
    }

    private static string GetAuditLabel(string action) => action switch
    {
        "MissedCallRecoveryScheduled" => "Missed call captured",
        "LeadAssigned" => "Assignment changed",
        "LeadStatusChanged" => "Status changed",
        "LeadAutomationPaused" => "Automation paused",
        "LeadAutomationResumed" => "Automation resumed",
        "ManualSmsQueued" => "Manual SMS queued",
        "BookingLinkQueued" => "Booking link queued",
        "ScheduledActionCancelled" => "Scheduled action cancelled",
        "LeadNoteAdded" => "Internal note added",
        "AiAnalysisAccepted" => "AI suggestion accepted",
        "AiAnalysisEdited" => "AI suggestion edited",
        "AiAnalysisRejected" => "AI suggestion rejected",
        _ => "Lead activity updated",
    };
}
