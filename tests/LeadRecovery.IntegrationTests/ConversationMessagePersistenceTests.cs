using System.Security.Claims;

using LeadRecovery.Application.Tenancy;
using LeadRecovery.Domain.Conversations;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class ConversationMessagePersistenceTests(LeadRecoveryApiFixture fixture)
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 14, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InboundAndOutboundMessagesPersistWithExpectedStates()
    {
        Guid tenantId = Guid.CreateVersion7();
        (Guid leadId, Guid conversationId) = await PersistConversationGraph(
            tenantId,
            "+14165550130");
        Message inbound = CreateInbound(
            tenantId,
            leadId,
            conversationId,
            "SM-inbound-persisted",
            "inbound-persisted");
        Message outbound = CreateOutbound(
            tenantId,
            leadId,
            conversationId,
            "outbound-persisted");
        outbound.MarkSent("SM-outbound-persisted", CreatedAtUtc.AddSeconds(1));
        outbound.MarkDelivered(CreatedAtUtc.AddSeconds(2));

        await PersistMessage(inbound);
        await PersistMessage(outbound);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        List<Message> messages = await dbContext.Messages
            .OrderBy(message => message.Direction)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, message =>
            message.Direction == MessageDirection.Inbound &&
            message.Status == MessageStatus.Received);
        Assert.Contains(messages, message =>
            message.Direction == MessageDirection.Outbound &&
            message.Status == MessageStatus.Delivered);
    }

    [Fact]
    public async Task ProviderMessageSidIsUniqueAcrossTenants()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        (Guid firstLeadId, Guid firstConversationId) = await PersistConversationGraph(
            firstTenantId,
            "+14165550131");
        (Guid secondLeadId, Guid secondConversationId) = await PersistConversationGraph(
            secondTenantId,
            "+14165550132");
        const string duplicateProviderSid = "SM-global-duplicate";

        await PersistMessage(CreateInbound(
            firstTenantId,
            firstLeadId,
            firstConversationId,
            duplicateProviderSid,
            "first-provider-event"));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            PersistMessage(CreateInbound(
                secondTenantId,
                secondLeadId,
                secondConversationId,
                duplicateProviderSid,
                "second-provider-event")));
    }

    [Fact]
    public async Task ClientIdempotencyKeyIsUniqueWithinTenantOnly()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        (Guid firstLeadId, Guid firstConversationId) = await PersistConversationGraph(
            firstTenantId,
            "+14165550133");
        (Guid secondLeadId, Guid secondConversationId) = await PersistConversationGraph(
            secondTenantId,
            "+14165550134");
        const string sharedClientKey = "shared-client-key";

        await PersistMessage(CreateOutbound(
            firstTenantId,
            firstLeadId,
            firstConversationId,
            sharedClientKey));
        await PersistMessage(CreateOutbound(
            secondTenantId,
            secondLeadId,
            secondConversationId,
            sharedClientKey));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            PersistMessage(CreateOutbound(
                firstTenantId,
                firstLeadId,
                firstConversationId,
                sharedClientKey)));
    }

    [Fact]
    public async Task CompoundForeignKeyRejectsCrossTenantLeadRelationship()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        _ = await PersistConversationGraph(firstTenantId, "+14165550135");
        (Guid secondLeadId, _) = await PersistConversationGraph(
            secondTenantId,
            "+14165550136");

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, firstTenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Conversations.Add(new Conversation(
            Guid.CreateVersion7(),
            firstTenantId,
            secondLeadId,
            ConversationChannel.Sms,
            CreatedAtUtc));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TenantFiltersIsolateLeadConversationAndMessageReads()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        (Guid firstLeadId, Guid firstConversationId) = await PersistConversationGraph(
            firstTenantId,
            "+14165550137");
        (Guid secondLeadId, Guid secondConversationId) = await PersistConversationGraph(
            secondTenantId,
            "+14165550138");
        await PersistMessage(CreateOutbound(
            firstTenantId,
            firstLeadId,
            firstConversationId,
            "first-visible"));
        await PersistMessage(CreateOutbound(
            secondTenantId,
            secondLeadId,
            secondConversationId,
            "second-visible"));

        Assert.Equal((1, 1, 1), await CountVisibleGraph(firstTenantId));
        Assert.Equal((1, 1, 1), await CountVisibleGraph(secondTenantId));
    }

    [Fact]
    public async Task CrossTenantMessageWriteFailsClosed()
    {
        Guid activeTenantId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();
        _ = await PersistConversationGraph(activeTenantId, "+14165550139");
        (Guid otherLeadId, Guid otherConversationId) = await PersistConversationGraph(
            otherTenantId,
            "+14165550140");

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, activeTenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Messages.Add(CreateOutbound(
            otherTenantId,
            otherLeadId,
            otherConversationId,
            "cross-tenant-write"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingTenantContextCannotQueryConversationData()
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        IHttpContextAccessor accessor =
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = null;
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();

        await Assert.ThrowsAsync<TenantContextUnavailableException>(() =>
            dbContext.Leads.CountAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TenantContextUnavailableException>(() =>
            dbContext.Conversations.CountAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TenantContextUnavailableException>(() =>
            dbContext.Messages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LeadPersistenceUsesOptimisticConcurrency()
    {
        Guid tenantId = Guid.CreateVersion7();
        (Guid leadId, _) = await PersistConversationGraph(tenantId, "+14165550141");

        using IServiceScope claimScope = fixture.Application.Services.CreateScope();
        using TenantClaimScope tenantClaim = new(claimScope.ServiceProvider, tenantId);
        await using AsyncServiceScope firstScope =
            fixture.Application.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext firstContext =
            firstScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        LeadRecoveryDbContext secondContext =
            secondScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Lead first = await firstContext.Leads.SingleAsync(
            lead => lead.Id == leadId,
            TestContext.Current.CancellationToken);
        Lead second = await secondContext.Leads.SingleAsync(
            lead => lead.Id == leadId,
            TestContext.Current.CancellationToken);

        first.BeginContacting(CreatedAtUtc.AddMinutes(1));
        second.BeginContacting(CreatedAtUtc.AddMinutes(2));
        await firstContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Version);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            secondContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private async Task<(Guid LeadId, Guid ConversationId)> PersistConversationGraph(
        Guid tenantId,
        string phoneE164)
    {
        await using (AsyncServiceScope tenantScope =
            fixture.Application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext tenantContext =
                tenantScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            tenantContext.Tenants.Add(new Tenant(
                tenantId,
                $"Tenant {tenantId:N}",
                $"tenant-{tenantId:N}",
                "America/Toronto",
                CreatedAtUtc));
            await tenantContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Guid leadId = Guid.CreateVersion7();
        Guid conversationId = Guid.CreateVersion7();
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Leads.Add(new Lead(
            leadId,
            tenantId,
            phoneE164,
            LeadSource.InboundSms,
            CreatedAtUtc));
        dbContext.Conversations.Add(new Conversation(
            conversationId,
            tenantId,
            leadId,
            ConversationChannel.Sms,
            CreatedAtUtc));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (leadId, conversationId);
    }

    private async Task PersistMessage(Message message)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, message.TenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(int Leads, int Conversations, int Messages)> CountVisibleGraph(
        Guid tenantId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        return (
            await dbContext.Leads.CountAsync(cancellationToken),
            await dbContext.Conversations.CountAsync(cancellationToken),
            await dbContext.Messages.CountAsync(cancellationToken));
    }

    private static Message CreateInbound(
        Guid tenantId,
        Guid leadId,
        Guid conversationId,
        string providerMessageSid,
        string clientIdempotencyKey) =>
        Message.ReceiveInbound(
            Guid.CreateVersion7(),
            tenantId,
            leadId,
            conversationId,
            MessageKind.System,
            "Twilio",
            providerMessageSid,
            clientIdempotencyKey,
            "Inbound test message",
            CreatedAtUtc);

    private static Message CreateOutbound(
        Guid tenantId,
        Guid leadId,
        Guid conversationId,
        string clientIdempotencyKey) =>
        Message.QueueOutbound(
            Guid.CreateVersion7(),
            tenantId,
            leadId,
            conversationId,
            MessageKind.Automated,
            "Twilio",
            clientIdempotencyKey,
            "Outbound test message",
            CreatedAtUtc);

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
