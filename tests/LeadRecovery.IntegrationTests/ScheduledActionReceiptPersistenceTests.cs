using System.Security.Claims;

using LeadRecovery.Application.Leads;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Integrations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class ScheduledActionReceiptPersistenceTests(LeadRecoveryApiFixture fixture)
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScheduledActionIdempotencyKeyIsUniqueWithinTenantOnly()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        Guid firstLeadId = await PersistTenantAndLead(firstTenantId, "+14165550150");
        Guid secondLeadId = await PersistTenantAndLead(secondTenantId, "+14165550151");
        const string sharedKey = "missed-call-follow-up:1";

        await PersistAction(CreateAction(firstTenantId, firstLeadId, sharedKey));
        await PersistAction(CreateAction(secondTenantId, secondLeadId, sharedKey));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            PersistAction(CreateAction(firstTenantId, firstLeadId, sharedKey)));
    }

    [Fact]
    public async Task ScheduledActionReadsAndWritesAreTenantIsolated()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        Guid firstLeadId = await PersistTenantAndLead(firstTenantId, "+14165550152");
        Guid secondLeadId = await PersistTenantAndLead(secondTenantId, "+14165550153");
        await PersistAction(CreateAction(firstTenantId, firstLeadId, "first-action"));
        await PersistAction(CreateAction(secondTenantId, secondLeadId, "second-action"));

        Assert.Equal(1, await CountVisibleActions(firstTenantId));
        Assert.Equal(1, await CountVisibleActions(secondTenantId));

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, firstTenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.ScheduledActions.Add(CreateAction(
            secondTenantId,
            secondLeadId,
            "cross-tenant-action"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompoundForeignKeyRejectsCrossTenantScheduledActionLead()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        _ = await PersistTenantAndLead(firstTenantId, "+14165550154");
        Guid secondLeadId = await PersistTenantAndLead(secondTenantId, "+14165550155");

        await Assert.ThrowsAsync<DbUpdateException>(() => PersistAction(
            CreateAction(firstTenantId, secondLeadId, "cross-tenant-lead")));
    }

    [Fact]
    public async Task BookingLeadPersistsLeadAndCancelsOnlyItsPendingActions()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid bookedLeadId = await PersistTenantAndLead(tenantId, "+14165550156");
        Guid otherLeadId = await PersistLead(tenantId, "+14165550157");
        ScheduledAction pending = CreateAction(tenantId, bookedLeadId, "pending-target");
        ScheduledAction running = CreateAction(tenantId, bookedLeadId, "running-target");
        running.Start(CreatedAtUtc.AddMinutes(1));
        ScheduledAction other = CreateAction(tenantId, otherLeadId, "pending-other");
        await PersistAction(pending);
        await PersistAction(running);
        await PersistAction(other);

        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            Lead lead = await dbContext.Leads.SingleAsync(
                candidate => candidate.Id == bookedLeadId,
                TestContext.Current.CancellationToken);
            PrepareForBooking(lead);
            BookLeadUseCase useCase =
                scope.ServiceProvider.GetRequiredService<BookLeadUseCase>();

            await useCase.ExecuteAsync(
                lead,
                CreatedAtUtc.AddMinutes(6),
                TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope verificationClaim =
            new(verificationScope.ServiceProvider, tenantId);
        LeadRecoveryDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Lead persistedLead = await verificationContext.Leads.SingleAsync(
            lead => lead.Id == bookedLeadId,
            TestContext.Current.CancellationToken);
        Dictionary<Guid, ScheduledActionStatus> statuses = await verificationContext
            .ScheduledActions
            .ToDictionaryAsync(
                action => action.Id,
                action => action.Status,
                TestContext.Current.CancellationToken);

        Assert.Equal(LeadStatus.Booked, persistedLead.Status);
        Assert.Equal(AutomationState.Completed, persistedLead.AutomationState);
        Assert.Equal(ScheduledActionStatus.Cancelled, statuses[pending.Id]);
        Assert.Equal(ScheduledActionStatus.Running, statuses[running.Id]);
        Assert.Equal(ScheduledActionStatus.Pending, statuses[other.Id]);
    }

    [Fact]
    public async Task ReceiptCanResolveTenantOnceWithoutARequestTenantContext()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        await PersistTenant(firstTenantId);
        await PersistTenant(secondTenantId);
        Guid receiptId = Guid.CreateVersion7();

        await using (AsyncServiceScope creationScope =
            fixture.Application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext creationContext =
                creationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            creationContext.ExternalEventReceipts.Add(CreateReceipt(receiptId, "event-unresolved"));
            await creationContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        ExternalEventReceipt receipt = await dbContext.ExternalEventReceipts.SingleAsync(
            candidate => candidate.Id == receiptId,
            TestContext.Current.CancellationToken);
        receipt.AssignTenant(firstTenantId);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        dbContext.Entry(receipt).Property(item => item.TenantId).CurrentValue = secondTenantId;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReceiptIdentityRejectsExactDuplicateButKeepsStatusProgression()
    {
        await PersistReceipt(CreateReceipt(
            Guid.CreateVersion7(),
            "CA123:no-answer:event-1"));
        await PersistReceipt(CreateReceipt(
            Guid.CreateVersion7(),
            "CA123:completed:event-2"));

        await Assert.ThrowsAsync<DbUpdateException>(() => PersistReceipt(CreateReceipt(
            Guid.CreateVersion7(),
            "CA123:no-answer:event-1")));
    }

    [Fact]
    public async Task MissingTenantContextFailsClosedOnlyForTenantOwnedActions()
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        IHttpContextAccessor accessor =
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = null;
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();

        await Assert.ThrowsAsync<TenantContextUnavailableException>(() =>
            dbContext.ScheduledActions.CountAsync(TestContext.Current.CancellationToken));
        _ = await dbContext.ExternalEventReceipts.CountAsync(
            TestContext.Current.CancellationToken);
    }

    private async Task<Guid> PersistTenantAndLead(Guid tenantId, string phoneE164)
    {
        await PersistTenant(tenantId);
        return await PersistLead(tenantId, phoneE164);
    }

    private async Task PersistTenant(Guid tenantId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.Add(new Tenant(
            tenantId,
            $"Tenant {tenantId:N}",
            $"tenant-{tenantId:N}",
            "America/Toronto",
            CreatedAtUtc));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> PersistLead(Guid tenantId, string phoneE164)
    {
        Guid leadId = Guid.CreateVersion7();
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Leads.Add(new Lead(
            leadId,
            tenantId,
            phoneE164,
            LeadSource.MissedCall,
            CreatedAtUtc));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return leadId;
    }

    private async Task PersistAction(ScheduledAction action)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, action.TenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.ScheduledActions.Add(action);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountVisibleActions(Guid tenantId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        return await dbContext.ScheduledActions.CountAsync(
            TestContext.Current.CancellationToken);
    }

    private async Task PersistReceipt(ExternalEventReceipt receipt)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.ExternalEventReceipts.Add(receipt);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static ScheduledAction CreateAction(
        Guid tenantId,
        Guid leadId,
        string idempotencyKey) =>
        new(
            Guid.CreateVersion7(),
            tenantId,
            leadId,
            "SendSms",
            CreatedAtUtc.AddHours(1),
            idempotencyKey,
            "{}",
            CreatedAtUtc);

    private static ExternalEventReceipt CreateReceipt(
        Guid id,
        string externalEventId) =>
        new(
            id,
            null,
            "Twilio",
            "CallStatus",
            externalEventId,
            $"sha256:{id:N}",
            CreatedAtUtc);

    private static void PrepareForBooking(Lead lead)
    {
        lead.BeginContacting(CreatedAtUtc.AddMinutes(1));
        lead.AwaitCustomer(CreatedAtUtc.AddMinutes(2));
        lead.Qualify(true, null, CreatedAtUtc.AddMinutes(3));
        lead.OfferBooking(CreatedAtUtc.AddMinutes(4));
    }

    private sealed class TenantClaimScope : IDisposable
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly HttpContext? _previousContext;

        public TenantClaimScope(IServiceProvider services, Guid tenantId)
        {
            _accessor = services.GetRequiredService<IHttpContextAccessor>();
            _previousContext = _accessor.HttpContext;
            ClaimsIdentity identity = new(
                [new Claim(TenantClaimTypes.TenantId, tenantId.ToString())],
                "IntegrationTest");
            _accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            };
        }

        public void Dispose()
        {
            _accessor.HttpContext = _previousContext;
        }
    }
}
