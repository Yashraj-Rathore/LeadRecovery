using System.Data;
using System.Text.Json;

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

internal sealed class ManualSmsWorkflowPersistence(
    LeadRecoveryDbContext dbContext,
    ITenantExecutionScope tenantExecutionScope)
    : IManualSmsWorkflowPersistence
{
    public async Task<PreparedOutboundSms?> PrepareManualOutboundAsync(
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
            action.ActionType != SmsScheduledActionTypes.SendManualSms ||
            action.Status != ScheduledActionStatus.Pending ||
            action.ScheduledForUtc > now)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (!TryReadMessageId(action.PayloadJson, out Guid messageId))
        {
            action.Start(now);
            action.Fail("The manual SMS action payload is invalid.", now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        Message? message = await dbContext.Messages.SingleOrDefaultAsync(
            candidate => candidate.Id == messageId,
            cancellationToken);
        Lead lead = await dbContext.Leads.SingleAsync(
            candidate => candidate.Id == action.LeadId,
            cancellationToken);
        Tenant tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == tenantId,
            cancellationToken);
        TenantPhoneNumber? sendingNumber = await dbContext.TenantPhoneNumbers
            .Where(number => number.InboundSmsEnabled)
            .OrderByDescending(number => number.IsPrimary)
            .ThenBy(number => number.Id)
            .FirstOrDefaultAsync(cancellationToken);
        Customer? customer = await dbContext.Customers.SingleOrDefaultAsync(
            candidate => candidate.PhoneE164 == lead.PrimaryPhoneE164,
            cancellationToken);

        bool eligible =
            tenant.Status is TenantStatus.Trial or TenantStatus.Active &&
            lead.AutomationState != AutomationState.SuppressedOptOut &&
            lead.Status is not (LeadStatus.Closed or LeadStatus.ClosedWon) &&
            customer?.OptedOutAtUtc is null &&
            sendingNumber is not null &&
            message is not null &&
            message.Kind == MessageKind.Manual &&
            message.Status == MessageStatus.Queued;
        if (!eligible)
        {
            if (message?.Status == MessageStatus.Queued)
            {
                message.Suppress();
            }

            action.Cancel(now);
            AddAudit(
                lead,
                "ManualSmsSuppressed",
                correlationId,
                now,
                new { messageId, reason = "ExecutionTimePolicyCheckFailed" });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        action.Start(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Message preparedMessage = message!;
        TenantPhoneNumber preparedSendingNumber = sendingNumber!;
        return new PreparedOutboundSms(
            tenantId,
            action.Id,
            preparedMessage.Id,
            new SmsSendRequest(
                preparedSendingNumber.PhoneNumberE164,
                lead.PrimaryPhoneE164,
                preparedMessage.Body,
                preparedMessage.ClientIdempotencyKey,
                statusCallbackUri));
    }

    public async Task<OutboundSmsOutcome> CompleteManualOutboundAsync(
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
                outcome = OutboundSmsOutcome.Accepted;
                break;
            case SmsSendDisposition.TransientFailure:
                action.Retry(now, NormalizeFailure(result, "Transient provider failure"), now);
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
            "ManualSmsProcessed",
            correlationId,
            now,
            new
            {
                messageId = message.Id,
                result = outcome.ToString(),
                failureCode = result.FailureCode,
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
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
            "SmsWorker",
            action,
            nameof(Lead),
            lead.Id.ToString("N"),
            correlationId,
            now,
            afterJson: JsonSerializer.Serialize(result)));

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

    private static string NormalizeFailure(SmsSendResult result, string fallback)
    {
        string value = string.Join(
            ": ",
            new[] { result.FailureCode, result.FailureDescription }
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate)));
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
