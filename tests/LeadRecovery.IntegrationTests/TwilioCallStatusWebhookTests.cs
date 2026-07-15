using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using LeadRecovery.Application.Integrations;
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
public sealed class TwilioCallStatusWebhookTests(LeadRecoveryApiFixture fixture)
{
    private const string AuthToken = "integration-test-twilio-auth-token";
    private const string CanonicalUrl =
        "https://webhooks.example.test/api/v1/webhooks/twilio/call-status";

    [Fact]
    public async Task ValidSignedFixtureUsesCanonicalProxyUrlAndSchedulesRecovery()
    {
        Dictionary<string, string> form = await LoadFixtureAsync();
        Guid tenantId = await PersistRouteAsync(form["To"], TenantStatus.Active);

        using HttpResponseMessage response = await PostAsync(form);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Lead lead = await dbContext.Leads.IgnoreQueryFilters().SingleAsync(
            candidate => candidate.TenantId == tenantId &&
                candidate.PrimaryPhoneE164 == form["From"],
            TestContext.Current.CancellationToken);
        ScheduledAction action = await dbContext.ScheduledActions
            .IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.TenantId == tenantId &&
                    candidate.LeadId == lead.Id,
                TestContext.Current.CancellationToken);
        Assert.Equal(ProcessCallStatusWebhookUseCase.RecoveryActionType, action.ActionType);
        Assert.Equal(ScheduledActionStatus.Pending, action.Status);
        Assert.Equal(lead.CreatedAtUtc.AddSeconds(30), action.ScheduledForUtc);
        Assert.Equal(
            1,
            await dbContext.ExternalEventReceipts.CountAsync(
                receipt => receipt.TenantId == tenantId,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await dbContext.AuditEvents.CountAsync(
                auditEvent => auditEvent.TenantId == tenantId &&
                    auditEvent.Action == "MissedCallRecoveryScheduled",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidSignatureReturnsForbiddenWithoutReceipt()
    {
        Dictionary<string, string> form = await LoadFixtureAsync();
        form["CallSid"] = CreateCallSid();
        int receiptCountBefore = await CountReceiptsAsync();
        using HttpClient client = fixture.Application.CreateClient();
        using FormUrlEncodedContent content = new(form);
        content.Headers.Add("X-Twilio-Signature", "invalid-signature");

        using HttpResponseMessage response = await client.PostAsync(
            "/api/v1/webhooks/twilio/call-status",
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(receiptCountBefore, await CountReceiptsAsync());
    }

    [Fact]
    public async Task DuplicateCallbackHasNoDuplicateEffect()
    {
        Dictionary<string, string> form = await LoadFixtureAsync();
        form["CallSid"] = CreateCallSid();
        form["To"] = "+14165550101";
        form["From"] = "+14165550124";
        Guid tenantId = await PersistRouteAsync(form["To"], TenantStatus.Active);

        using HttpResponseMessage first = await PostAsync(form);
        using HttpResponseMessage duplicate = await PostAsync(form);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        Counts counts = await CountTenantDataAsync(tenantId);
        Assert.Equal(1, counts.Leads);
        Assert.Equal(1, counts.Actions);
        Assert.Equal(1, counts.Receipts);
    }

    [Fact]
    public async Task CooldownPreventsSecondRecoveryAction()
    {
        Dictionary<string, string> firstForm = await LoadFixtureAsync();
        firstForm["CallSid"] = CreateCallSid();
        firstForm["To"] = "+14165550102";
        firstForm["From"] = "+14165550125";
        Guid tenantId = await PersistRouteAsync(firstForm["To"], TenantStatus.Active);
        Dictionary<string, string> secondForm = new(firstForm, StringComparer.Ordinal)
        {
            ["CallSid"] = CreateCallSid(),
        };

        using HttpResponseMessage first = await PostAsync(firstForm);
        using HttpResponseMessage second = await PostAsync(secondForm);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Counts counts = await CountTenantDataAsync(tenantId);
        Assert.Equal(1, counts.Leads);
        Assert.Equal(1, counts.Actions);
        Assert.Equal(2, counts.Receipts);
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Equal(
            1,
            await dbContext.ExternalEventReceipts.CountAsync(
                receipt => receipt.TenantId == tenantId &&
                    receipt.ProcessingResult ==
                        CallStatusProcessingOutcome.IgnoredCooldown.ToString(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuspendedTenantIsAcknowledgedWithoutLeadOrAction()
    {
        Dictionary<string, string> form = await LoadFixtureAsync();
        form["CallSid"] = CreateCallSid();
        form["To"] = "+14165550103";
        form["From"] = "+14165550126";
        Guid tenantId = await PersistRouteAsync(form["To"], TenantStatus.Suspended);

        using HttpResponseMessage response = await PostAsync(form);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Counts counts = await CountTenantDataAsync(tenantId);
        Assert.Equal(0, counts.Leads);
        Assert.Equal(0, counts.Actions);
        Assert.Equal(1, counts.Receipts);
    }

    [Fact]
    public async Task UnknownDestinationIsAcknowledgedWithoutTenantBusinessData()
    {
        Dictionary<string, string> form = await LoadFixtureAsync();
        form["CallSid"] = CreateCallSid();
        form["To"] = "+14165550104";
        form["From"] = "+14165550127";
        int leadsBefore = await CountAllLeadsAsync();
        int actionsBefore = await CountAllActionsAsync();

        using HttpResponseMessage response = await PostAsync(form);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(leadsBefore, await CountAllLeadsAsync());
        Assert.Equal(actionsBefore, await CountAllActionsAsync());
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Equal(
            1,
            await dbContext.ExternalEventReceipts.CountAsync(
                receipt => receipt.TenantId == null &&
                    receipt.EventType == "CallStatus" &&
                    receipt.ProcessingResult ==
                        CallStatusProcessingOutcome.IgnoredUnknownNumber.ToString(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProviderDestinationCannotMapToTwoTenants()
    {
        const string destination = "+14165550105";
        _ = await PersistRouteAsync(destination, TenantStatus.Active);

        _ = await Assert.ThrowsAsync<DbUpdateException>(() =>
            PersistRouteAsync(destination, TenantStatus.Active));

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Equal(
            1,
            await dbContext.TenantPhoneNumbers.IgnoreQueryFilters().CountAsync(
                number => number.Provider == "Twilio" &&
                    number.PhoneNumberE164 == destination,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NonRecoverableStatusIsAcknowledgedWithoutRecoveryAction()
    {
        Dictionary<string, string> form = await LoadFixtureAsync();
        form["CallSid"] = CreateCallSid();
        form["CallStatus"] = "completed";
        form["To"] = "+14165550106";
        form["From"] = "+14165550128";
        Guid tenantId = await PersistRouteAsync(form["To"], TenantStatus.Active);

        using HttpResponseMessage response = await PostAsync(form);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Counts counts = await CountTenantDataAsync(tenantId);
        Assert.Equal(0, counts.Leads);
        Assert.Equal(0, counts.Actions);
        Assert.Equal(1, counts.Receipts);
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Equal(
            1,
            await dbContext.ExternalEventReceipts.CountAsync(
                receipt => receipt.TenantId == tenantId &&
                    receipt.ProcessingResult ==
                        CallStatusProcessingOutcome.IgnoredStatus.ToString(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TenantPhoneNumberReadsAndWritesRemainTenantScoped()
    {
        const string firstDestination = "+14165550107";
        Guid firstTenantId = await PersistRouteAsync(
            firstDestination,
            TenantStatus.Active);
        Guid secondTenantId = await PersistRouteAsync(
            "+14165550108",
            TenantStatus.Active);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, secondTenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.False(await dbContext.TenantPhoneNumbers.AnyAsync(
            number => number.PhoneNumberE164 == firstDestination,
            TestContext.Current.CancellationToken));

        dbContext.TenantPhoneNumbers.Add(new TenantPhoneNumber(
            Guid.CreateVersion7(),
            firstTenantId,
            "Twilio",
            "+14165550109",
            $"PN{Guid.NewGuid():N}",
            ["no-answer"],
            30,
            300));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private async Task<Guid> PersistRouteAsync(
        string destinationPhoneE164,
        TenantStatus tenantStatus)
    {
        Guid tenantId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Tenant tenant = new(
            tenantId,
            $"Webhook tenant {tenantId:N}",
            $"webhook-{tenantId:N}",
            "America/Toronto",
            now);
        tenant.ChangeStatus(tenantStatus, now);
        tenant.SetAutomationEnabled(true, now);
        TenantPhoneNumber number = new(
            Guid.CreateVersion7(),
            tenantId,
            "Twilio",
            destinationPhoneE164,
            $"PN{Guid.NewGuid():N}",
            ["no-answer", "busy", "failed"],
            initialDelaySeconds: 30,
            recoveryCooldownSeconds: 300,
            isPrimary: true);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.Tenants.Add(tenant);
        dbContext.TenantPhoneNumbers.Add(number);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return tenantId;
    }

    private async Task<HttpResponseMessage> PostAsync(Dictionary<string, string> form)
    {
        using HttpClient client = fixture.Application.CreateClient();
        using FormUrlEncodedContent content = new(form);
        content.Headers.Add("X-Twilio-Signature", ComputeSignature(form));
        return await client.PostAsync(
            "/api/v1/webhooks/twilio/call-status",
            content,
            TestContext.Current.CancellationToken);
    }

    private static string ComputeSignature(IReadOnlyDictionary<string, string> form)
    {
        StringBuilder signedValue = new(CanonicalUrl);
        foreach ((string key, string value) in
            form.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _ = signedValue.Append(key).Append(value);
        }

#pragma warning disable CA5350 // Twilio's X-Twilio-Signature protocol requires HMAC-SHA1.
        using HMACSHA1 hmac = new(Encoding.UTF8.GetBytes(AuthToken));
#pragma warning restore CA5350
        return Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(signedValue.ToString())));
    }

    private static async Task<Dictionary<string, string>> LoadFixtureAsync()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Twilio",
            "call-status-no-answer.json");
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
                stream,
                cancellationToken: TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The Twilio fixture was empty.");
    }

    private static string CreateCallSid() => $"CA{Guid.NewGuid():N}";

    private async Task<Counts> CountTenantDataAsync(Guid tenantId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        return new Counts(
            await dbContext.Leads.IgnoreQueryFilters().CountAsync(
                lead => lead.TenantId == tenantId,
                TestContext.Current.CancellationToken),
            await dbContext.ScheduledActions.IgnoreQueryFilters().CountAsync(
                action => action.TenantId == tenantId,
                TestContext.Current.CancellationToken),
            await dbContext.ExternalEventReceipts.CountAsync(
                receipt => receipt.TenantId == tenantId,
                TestContext.Current.CancellationToken));
    }

    private async Task<int> CountReceiptsAsync()
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>()
            .ExternalEventReceipts.CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountAllLeadsAsync()
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>()
            .Leads.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountAllActionsAsync()
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>()
            .ScheduledActions.IgnoreQueryFilters()
            .CountAsync(TestContext.Current.CancellationToken);
    }

    private sealed record Counts(int Leads, int Actions, int Receipts);

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

        public void Dispose() => _accessor.HttpContext = _previousContext;
    }
}
