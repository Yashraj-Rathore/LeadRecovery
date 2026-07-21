using System.Collections.Concurrent;

using Hangfire;

using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace LeadRecovery.Worker;

internal sealed partial class ScheduledActionDispatcher(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobClient backgroundJobs,
    SmsWorkerOptions options,
    TimeProvider timeProvider,
    ILogger<ScheduledActionDispatcher> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _recentlyEnqueued = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDispatchFailed(logger, exception);
            }

            await Task.Delay(options.DispatchInterval, timeProvider, stoppingToken);
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();

        DateTimeOffset staleBefore = now.Subtract(options.RunningLease);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            update scheduled_actions
            set status = 'Pending',
                scheduled_for_utc = {now},
                last_error = 'Recovered expired worker lease.',
                updated_at_utc = {now}
            where status = 'Running'
              and updated_at_utc <= {staleBefore}
              and action_type in (
                  {ProcessCallStatusWebhookUseCase.RecoveryActionType},
                  {SmsScheduledActionTypes.SendManualSms},
                  {WorkflowScheduledActionTypes.SendQualificationQuestion},
                  {WorkflowScheduledActionTypes.SendBookingLink},
                  {WorkflowScheduledActionTypes.SendFollowUpSms})
            """,
            cancellationToken);

        var dueActions = await dbContext.ScheduledActions
            .IgnoreQueryFilters()
            .Where(action =>
                (action.ActionType == ProcessCallStatusWebhookUseCase.RecoveryActionType ||
                    action.ActionType == SmsScheduledActionTypes.SendManualSms ||
                    action.ActionType ==
                        WorkflowScheduledActionTypes.SendQualificationQuestion ||
                    action.ActionType == WorkflowScheduledActionTypes.SendBookingLink ||
                    action.ActionType == WorkflowScheduledActionTypes.SendFollowUpSms) &&
                action.Status == ScheduledActionStatus.Pending &&
                action.ScheduledForUtc <= now)
            .OrderBy(action => action.ScheduledForUtc)
            .ThenBy(action => action.Id)
            .Select(action => new { action.Id, action.TenantId, action.ActionType })
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var action in dueActions)
        {
            if (_recentlyEnqueued.TryGetValue(action.Id, out DateTimeOffset enqueuedAt) &&
                enqueuedAt > now.Subtract(options.RunningLease))
            {
                continue;
            }

            string correlationId = $"worker:{action.Id:N}:{now.ToUnixTimeSeconds()}";
            if (action.ActionType == SmsScheduledActionTypes.SendManualSms)
            {
                _ = backgroundJobs.Enqueue<ScheduledManualSmsJob>(job => job.ExecuteAsync(
                    action.Id,
                    action.TenantId,
                    correlationId,
                    CancellationToken.None));
            }
            else if (WorkflowScheduledActionTypes.IsWorkflowSms(action.ActionType))
            {
                _ = backgroundJobs.Enqueue<ScheduledWorkflowSmsJob>(job => job.ExecuteAsync(
                    action.Id,
                    action.TenantId,
                    correlationId,
                    CancellationToken.None));
            }
            else
            {
                _ = backgroundJobs.Enqueue<ScheduledRecoverySmsJob>(job => job.ExecuteAsync(
                    action.Id,
                    action.TenantId,
                    correlationId,
                    CancellationToken.None));
            }
            _recentlyEnqueued[action.Id] = now;
            LogEnqueued(logger, action.Id, action.TenantId);
        }
    }

    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Error,
        Message = "Scheduled-action dispatch failed.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Information,
        Message = "Enqueued scheduled action {ScheduledActionId} for tenant {TenantId}.")]
    private static partial void LogEnqueued(
        ILogger logger,
        Guid scheduledActionId,
        Guid tenantId);
}
