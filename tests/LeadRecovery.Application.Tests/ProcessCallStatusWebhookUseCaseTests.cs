using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Audit;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Tests;

public sealed class ProcessCallStatusWebhookUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecoverableCallCreatesLeadAndDurableAction()
    {
        Guid tenantId = Guid.CreateVersion7();
        InMemoryCallStatusPersistence persistence = new()
        {
            Route = new TenantPhoneRecoveryRoute(
                tenantId,
                true,
                ["no-answer"],
                30,
                300),
        };
        RecordingTenantScope tenantScope = new();
        RecordingMetrics metrics = new();
        ProcessCallStatusWebhookUseCase useCase = new(
            persistence,
            tenantScope,
            metrics,
            new FixedTimeProvider(Now));

        CallStatusProcessingOutcome outcome = await useCase.ExecuteAsync(
            CreateEvent(),
            TestContext.Current.CancellationToken);

        Assert.Equal(CallStatusProcessingOutcome.RecoveryScheduled, outcome);
        Lead lead = Assert.Single(persistence.Leads);
        Assert.Equal(tenantId, lead.TenantId);
        Assert.Equal("+14165550123", lead.PrimaryPhoneE164);
        ScheduledAction action = Assert.Single(persistence.Actions);
        Assert.Equal(lead.Id, action.LeadId);
        Assert.Equal(Now.AddSeconds(30), action.ScheduledForUtc);
        Assert.Equal(ScheduledActionStatus.Pending, action.Status);
        Assert.Equal(tenantId, tenantScope.LastTenantId);
        Assert.Equal(outcome, metrics.LastOutcome);
        Assert.Equal("RecoveryScheduled", persistence.Receipt?.ProcessingResult);
        Assert.Single(persistence.AuditEvents);
    }

    [Fact]
    public async Task CooldownUpdatesExistingLeadWithoutCreatingAction()
    {
        Guid tenantId = Guid.CreateVersion7();
        Lead existing = new(
            Guid.CreateVersion7(),
            tenantId,
            "+14165550123",
            LeadSource.MissedCall,
            Now.AddMinutes(-10));
        InMemoryCallStatusPersistence persistence = new()
        {
            Route = new TenantPhoneRecoveryRoute(
                tenantId,
                true,
                ["no-answer"],
                30,
                300),
            LatestLead = existing,
            HasRecentRecoveryAction = true,
        };
        ProcessCallStatusWebhookUseCase useCase = new(
            persistence,
            new RecordingTenantScope(),
            new RecordingMetrics(),
            new FixedTimeProvider(Now));

        CallStatusProcessingOutcome outcome = await useCase.ExecuteAsync(
            CreateEvent(),
            TestContext.Current.CancellationToken);

        Assert.Equal(CallStatusProcessingOutcome.IgnoredCooldown, outcome);
        Assert.Equal(Now, existing.LastCustomerActivityAtUtc);
        Assert.Empty(persistence.Leads);
        Assert.Empty(persistence.Actions);
    }

    [Fact]
    public async Task DuplicateStopsBeforeRoutingOrBusinessWrites()
    {
        InMemoryCallStatusPersistence persistence = new()
        {
            ReceiptInserted = false,
        };
        ProcessCallStatusWebhookUseCase useCase = new(
            persistence,
            new RecordingTenantScope(),
            new RecordingMetrics(),
            new FixedTimeProvider(Now));

        CallStatusProcessingOutcome outcome = await useCase.ExecuteAsync(
            CreateEvent(),
            TestContext.Current.CancellationToken);

        Assert.Equal(CallStatusProcessingOutcome.Duplicate, outcome);
        Assert.Equal(0, persistence.RouteQueryCount);
        Assert.Empty(persistence.Leads);
        Assert.Empty(persistence.Actions);
        Assert.Empty(persistence.AuditEvents);
    }

    private static CallStatusWebhookEvent CreateEvent() =>
        new(
            "Twilio",
            "CA00000000000000000000000000000001",
            "no-answer",
            "+14165550123",
            "+14165550100",
            "sha256:event",
            "sha256:payload",
            "correlation-id");

    private sealed class InMemoryCallStatusPersistence : ICallStatusPersistence
    {
        public bool ReceiptInserted { get; init; } = true;

        public TenantPhoneRecoveryRoute? Route { get; init; }

        public Lead? LatestLead { get; init; }

        public bool HasRecentRecoveryAction { get; init; }

        public int RouteQueryCount { get; private set; }

        public ExternalEventReceipt? Receipt { get; private set; }

        public List<Lead> Leads { get; } = [];

        public List<ScheduledAction> Actions { get; } = [];

        public List<AuditEvent> AuditEvents { get; } = [];

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<bool> TryAddReceiptAsync(
            ExternalEventReceipt receipt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Receipt = receipt;
            return Task.FromResult(ReceiptInserted);
        }

        public Task<TenantPhoneRecoveryRoute?> FindRouteAsync(
            string provider,
            string destinationPhoneE164,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RouteQueryCount++;
            return Task.FromResult(Route);
        }

        public Task<Lead?> FindLatestLeadAsync(
            string callerPhoneE164,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LatestLead);
        }

        public Task<bool> HasRecoveryActionSinceAsync(
            string callerPhoneE164,
            DateTimeOffset sinceUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(HasRecentRecoveryAction);
        }

        public void AddLead(Lead lead) => Leads.Add(lead);

        public void AddScheduledAction(ScheduledAction action) => Actions.Add(action);

        public void AddAuditEvent(AuditEvent auditEvent) => AuditEvents.Add(auditEvent);
    }

    private sealed class RecordingTenantScope : ITenantExecutionScope
    {
        public Guid? LastTenantId { get; private set; }

        public IDisposable Begin(Guid tenantId)
        {
            LastTenantId = tenantId;
            return new DisposableAction();
        }
    }

    private sealed class DisposableAction : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class RecordingMetrics : ICallStatusMetrics
    {
        public CallStatusProcessingOutcome? LastOutcome { get; private set; }

        public void RecordSignatureRejected()
        {
        }

        public void RecordOutcome(CallStatusProcessingOutcome outcome) =>
            LastOutcome = outcome;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
