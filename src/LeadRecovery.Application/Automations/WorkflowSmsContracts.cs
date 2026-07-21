using System.Text.Json;

using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;

namespace LeadRecovery.Application.Automations;

public static class WorkflowScheduledActionTypes
{
    public const string SendQualificationQuestion = "SendQualificationQuestion";
    public const string SendBookingLink = "SendBookingLink";
    public const string SendFollowUpSms = "SendFollowUpSms";

    public static bool IsWorkflowSms(string actionType) =>
        actionType is SendQualificationQuestion or SendBookingLink or SendFollowUpSms;
}

public sealed record WorkflowScheduledActionPayload(
    int SchemaVersion,
    string Stage,
    string? QuestionKey,
    int? FollowUpSequence,
    string? TemplatePurpose,
    DateTimeOffset? BaselineCustomerActivityAtUtc);

public static class WorkflowScheduledActionPayloadSerializer
{
    public static string Serialize(WorkflowScheduledActionPayload payload) =>
        JsonSerializer.Serialize(payload);

    public static bool TryDeserialize(
        string json,
        out WorkflowScheduledActionPayload? payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<WorkflowScheduledActionPayload>(json);
            return payload is { SchemaVersion: 1 } &&
                !string.IsNullOrWhiteSpace(payload.Stage);
        }
        catch (JsonException)
        {
            payload = null;
            return false;
        }
    }
}

public interface IWorkflowActionScheduler
{
    Task<bool> ScheduleFirstQualificationAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> ScheduleQualificationQuestionAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        string questionKey,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> ScheduleBookingLinkAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        string stage,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> ScheduleFollowUpsAsync(
        Tenant tenant,
        Lead lead,
        WorkflowDefinition workflow,
        string stage,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> CancelPendingFollowUpsAsync(
        Guid leadId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
