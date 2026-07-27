using System.Data;
using System.Text.Json;

using LeadRecovery.Application.Analysis;
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

namespace LeadRecovery.Infrastructure.Persistence.Analysis;

internal sealed class LeadAnalysisWorkflowPersistence(
    LeadRecoveryDbContext dbContext,
    ITenantExecutionScope tenantExecutionScope,
    ILeadAnalysisInputHasher inputHasher)
    : ILeadAnalysisWorkflowPersistence
{
    private const int MaximumInputTurns = 8;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PreparedLeadAnalysis?> PrepareAsync(
        Guid actionId,
        Guid tenantId,
        string correlationId,
        DateTimeOffset now,
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
            action.ActionType != LeadAnalysisScheduledActionTypes.AnalyzeLead ||
            action.Status != ScheduledActionStatus.Pending ||
            action.ScheduledForUtc > now)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        Lead lead = await dbContext.Leads.SingleAsync(
            candidate => candidate.Id == action.LeadId,
            cancellationToken);
        if (action.AttemptCount > 0)
        {
            action.Start(now);
            action.Fail("AI analysis was not repeated after an expired worker lease.", now);
            bool routedToHuman = RouteToHumanReview(lead, now);
            AddAudit(
                tenantId,
                "AiAnalysisRepeatSuppressed",
                nameof(Lead),
                lead.Id,
                correlationId,
                now,
                new
                {
                    result = "worker_lease_recovered",
                    routedToHuman,
                    action.AttemptCount,
                });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (!LeadAnalysisScheduledActionPayloadSerializer.TryDeserialize(
                action.PayloadJson,
                out LeadAnalysisScheduledActionPayload? payload))
        {
            await FailBeforeProvider(
                action,
                lead,
                correlationId,
                now,
                "invalid_action_payload",
                transaction,
                cancellationToken);
            return null;
        }

        Tenant tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == tenantId,
            cancellationToken);
        Customer? customer = lead.CustomerId is null
            ? null
            : await dbContext.Customers.SingleOrDefaultAsync(
                candidate => candidate.Id == lead.CustomerId,
                cancellationToken);
        bool eligible =
            tenant.Status is TenantStatus.Trial or TenantStatus.Active &&
            tenant.AutomationEnabled &&
            lead.AutomationState == AutomationState.Active &&
            lead.Status is not (LeadStatus.Booked or LeadStatus.Closed or LeadStatus.ClosedWon) &&
            customer?.OptedOutAtUtc is null;
        if (!eligible)
        {
            action.Cancel(now);
            AddAudit(
                tenantId,
                "AiAnalysisCancelled",
                nameof(Lead),
                lead.Id,
                correlationId,
                now,
                new { result = "execution_time_eligibility_failed" });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        WorkflowDefinition? workflow = await dbContext.WorkflowDefinitions
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == payload!.WorkflowDefinitionId &&
                    candidate.Version == payload.WorkflowVersion &&
                    candidate.IsActive,
                cancellationToken);
        if (workflow is null ||
            !CategorySnapshotMatches(
                workflow,
                payload!.CategoryQuestionKey,
                payload.AllowedCategories))
        {
            action.Cancel(now);
            AddAudit(
                tenantId,
                "AiAnalysisCancelled",
                nameof(Lead),
                lead.Id,
                correlationId,
                now,
                new { result = "workflow_snapshot_no_longer_active" });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        Message? sourceMessage = await dbContext.Messages.SingleOrDefaultAsync(
            candidate =>
                candidate.Id == payload.SourceMessageId &&
                candidate.LeadId == lead.Id &&
                candidate.Direction == MessageDirection.Inbound &&
                candidate.Status == MessageStatus.Received,
            cancellationToken);
        if (sourceMessage is null)
        {
            await FailBeforeProvider(
                action,
                lead,
                correlationId,
                now,
                "source_message_unavailable",
                transaction,
                cancellationToken);
            return null;
        }

        ConversationTurn[] turns = (await dbContext.Messages
                .Where(candidate =>
                    candidate.LeadId == lead.Id &&
                    candidate.CreatedAtUtc <= sourceMessage.CreatedAtUtc &&
                    (candidate.Status == MessageStatus.Received ||
                        candidate.Status == MessageStatus.Sent ||
                        candidate.Status == MessageStatus.Delivered))
                .OrderByDescending(candidate => candidate.CreatedAtUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Take(MaximumInputTurns)
                .Select(candidate => new
                {
                    candidate.Direction,
                    candidate.Body,
                })
                .ToArrayAsync(cancellationToken))
            .Reverse()
            .Select(message => new ConversationTurn(
                message.Direction == MessageDirection.Inbound
                    ? ConversationParticipant.Customer
                    : ConversationParticipant.Business,
                message.Body))
            .ToArray();
        LeadAnalysisRequest request = new(
            tenantId,
            payload.AllowedCategories,
            turns,
            payload.AnalysisSchemaVersion);
        string inputHash = inputHasher.ComputeHash(request);

        bool alreadyAnalyzed = await dbContext.AiAnalyses.AnyAsync(
            candidate =>
                candidate.LeadId == lead.Id &&
                candidate.SchemaVersion == request.SchemaVersion &&
                candidate.InputHash == inputHash,
            cancellationToken);
        action.Start(now);
        if (alreadyAnalyzed)
        {
            action.Complete(now);
            AddAudit(
                tenantId,
                "AiAnalysisDuplicateSkipped",
                nameof(ScheduledAction),
                action.Id,
                correlationId,
                now,
                new { result = "existing_input_hash" });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PreparedLeadAnalysis(
            tenantId,
            action.Id,
            lead.Id,
            inputHash,
            request);
    }

    public async Task<LeadAnalysisWorkflowOutcome> CompleteAsync(
        PreparedLeadAnalysis prepared,
        LeadAnalysisResult result,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(result);
        using IDisposable tenantScope = tenantExecutionScope.Begin(prepared.TenantId);
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        ScheduledAction? action = await dbContext.ScheduledActions
            .FromSqlInterpolated(
                $"select * from scheduled_actions where id = {prepared.ActionId} and tenant_id = {prepared.TenantId} for update")
            .SingleOrDefaultAsync(cancellationToken);
        if (action is null ||
            action.ActionType != LeadAnalysisScheduledActionTypes.AnalyzeLead ||
            action.Status != ScheduledActionStatus.Running)
        {
            await transaction.CommitAsync(cancellationToken);
            return LeadAnalysisWorkflowOutcome.Ignored;
        }

        Lead lead = await dbContext.Leads.SingleAsync(
            candidate => candidate.Id == prepared.LeadId,
            cancellationToken);
        LeadAnalysisWorkflowOutcome outcome;
        if (result.Succeeded &&
            result.Suggestion is not null &&
            string.Equals(
                result.Suggestion.SchemaVersion,
                prepared.Request.SchemaVersion,
                StringComparison.Ordinal))
        {
            bool alreadyAnalyzed = await dbContext.AiAnalyses.AnyAsync(
                candidate =>
                    candidate.LeadId == prepared.LeadId &&
                    candidate.SchemaVersion == prepared.Request.SchemaVersion &&
                    candidate.InputHash == prepared.InputHash,
                cancellationToken);
            if (!alreadyAnalyzed)
            {
                LeadAnalysisSuggestion suggestion = result.Suggestion;
                AiAnalysis analysis = new(
                    Guid.CreateVersion7(),
                    prepared.TenantId,
                    prepared.LeadId,
                    prepared.Request.SchemaVersion,
                    result.Provider,
                    result.ModelReference,
                    prepared.InputHash,
                    prepared.Request.AllowedCategories,
                    new AiAnalysisValues(
                        suggestion.ServiceCategory,
                        suggestion.Urgency,
                        suggestion.Summary,
                        suggestion.Extracted.City,
                        suggestion.Extracted.PostalCode,
                        suggestion.Extracted.PreferredCallbackWindow,
                        suggestion.SuggestedReply),
                    suggestion.Confidence,
                    suggestion.RequiresHumanReview,
                    suggestion.ReasonCodes,
                    now);
                dbContext.AiAnalyses.Add(analysis);
                bool routedToHuman =
                    suggestion.RequiresHumanReview && RouteToHumanReview(lead, now);
                AddAudit(
                    prepared.TenantId,
                    "AiAnalysisCreated",
                    nameof(AiAnalysis),
                    analysis.Id,
                    correlationId,
                    now,
                    new
                    {
                        actionId = action.Id,
                        result = "success",
                        suggestion.RequiresHumanReview,
                        routedToHuman,
                        attempts = result.AttemptCount,
                    });
                outcome = routedToHuman
                    ? LeadAnalysisWorkflowOutcome.PersistedNeedsHuman
                    : LeadAnalysisWorkflowOutcome.Persisted;
            }
            else
            {
                AddAudit(
                    prepared.TenantId,
                    "AiAnalysisDuplicateSkipped",
                    nameof(ScheduledAction),
                    action.Id,
                    correlationId,
                    now,
                    new { result = "existing_input_hash" });
                outcome = LeadAnalysisWorkflowOutcome.Ignored;
            }

            action.Complete(now);
        }
        else
        {
            LeadAnalysisFailure failure = result.Failure ??
                new LeadAnalysisFailure(
                    LeadAnalysisFailureKind.InvalidOutput,
                    "analysis_schema_mismatch",
                    IsRetryable: false);
            string failureCode = NormalizeFailureCode(failure.Code);
            action.Fail(
                $"AI analysis failed ({failure.Kind}): {failureCode}.",
                now);
            bool routedToHuman = RouteToHumanReview(lead, now);
            AddAudit(
                prepared.TenantId,
                "AiAnalysisFailed",
                nameof(Lead),
                lead.Id,
                correlationId,
                now,
                new
                {
                    result = failure.Kind.ToString(),
                    failureCode,
                    routedToHuman,
                    attempts = result.AttemptCount,
                });
            outcome = routedToHuman
                ? LeadAnalysisWorkflowOutcome.FallbackNeedsHuman
                : LeadAnalysisWorkflowOutcome.FallbackRecorded;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private async Task FailBeforeProvider(
        ScheduledAction action,
        Lead lead,
        string correlationId,
        DateTimeOffset now,
        string failureCode,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        action.Start(now);
        action.Fail($"AI analysis failed before provider invocation: {failureCode}.", now);
        bool routedToHuman = RouteToHumanReview(lead, now);
        AddAudit(
            action.TenantId,
            "AiAnalysisFailed",
            nameof(Lead),
            lead.Id,
            correlationId,
            now,
            new
            {
                result = "preparation_failed",
                failureCode,
                routedToHuman,
                attempts = action.AttemptCount,
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool CategorySnapshotMatches(
        WorkflowDefinition workflow,
        string categoryQuestionKey,
        IReadOnlyCollection<string> expectedCategories)
    {
        QualificationQuestionPolicy? categoryQuestion = workflow
            .GetQualificationQuestions()
            .SingleOrDefault(question =>
                question.AnswerKind == QualificationAnswerKind.Choice &&
                question.Key.Equals(categoryQuestionKey, StringComparison.OrdinalIgnoreCase));
        return categoryQuestion is not null && categoryQuestion.AllowedValues
            .SequenceEqual(expectedCategories, StringComparer.Ordinal);
    }

    private static bool RouteToHumanReview(Lead lead, DateTimeOffset now)
    {
        if (lead.Status == LeadStatus.NeedsHuman)
        {
            return true;
        }

        if (lead.Status is LeadStatus.New or
            LeadStatus.Contacting or
            LeadStatus.AwaitingCustomer or
            LeadStatus.Qualified or
            LeadStatus.BookingOffered)
        {
            lead.RequireHumanReview(now);
            return true;
        }

        return false;
    }

    private static string NormalizeFailureCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "analysis_failure";
        }

        string normalized = new(
            value.Trim()
                .Take(100)
                .Select(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
                        ? char.ToLowerInvariant(character)
                        : '_')
                .ToArray());
        return string.IsNullOrWhiteSpace(normalized)
            ? "analysis_failure"
            : normalized;
    }

    private void AddAudit(
        Guid tenantId,
        string action,
        string entityType,
        Guid entityId,
        string correlationId,
        DateTimeOffset now,
        object after)
    {
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.CreateVersion7(),
            tenantId,
            "System",
            null,
            action,
            entityType,
            entityId.ToString(),
            correlationId,
            now,
            afterJson: JsonSerializer.Serialize(after, SerializerOptions)));
    }
}
