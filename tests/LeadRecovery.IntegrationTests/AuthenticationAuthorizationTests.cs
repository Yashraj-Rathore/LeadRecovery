using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

using LeadRecovery.Application.Authorization;
using LeadRecovery.Application.Automations;
using LeadRecovery.Application.Integrations;
using LeadRecovery.Application.Messaging;
using LeadRecovery.Application.Tenancy;
using LeadRecovery.Contracts.Authentication;
using LeadRecovery.Contracts.Automations;
using LeadRecovery.Contracts.Leads;
using LeadRecovery.Domain.Analysis;
using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Customers;
using LeadRecovery.Domain.Identity;
using LeadRecovery.Domain.Leads;
using LeadRecovery.Domain.Tenancy;
using LeadRecovery.Infrastructure.Identity;
using LeadRecovery.Infrastructure.Persistence;
using LeadRecovery.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadRecovery.IntegrationTests;

[Collection(PostgreSqlIntegrationDefinition.Name)]
public sealed class AuthenticationAuthorizationTests(LeadRecoveryApiFixture fixture)
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 14, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Staff)]
    public async Task OwnerAndStaffCanAuthenticate(TenantRole role)
    {
        TenantData tenant = await SeedTenant();
        UserData user = await SeedUser(tenant, role, createLead: false);
        using HttpClient client = CreateClient();

        LoginResult login = await Login(client, user);
        AuthSessionResponse? current = await client.GetFromJsonAsync<AuthSessionResponse>(
            "/api/v1/auth/me",
            TestContext.Current.CancellationToken);

        Assert.Equal(user.UserId, login.Session.UserId);
        Assert.Equal(tenant.TenantId, login.Session.TenantId);
        Assert.Equal(role.ToString(), login.Session.Role);
        Assert.NotNull(current);
        Assert.Equal(login.Session, current);
        Assert.Contains("HttpOnly", login.SessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", login.SessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", login.SessionCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LogoutInvalidatesPreviouslyIssuedCookieAndRecordsAudit()
    {
        TenantData tenant = await SeedTenant();
        UserData user = await SeedUser(tenant, TenantRole.Owner, createLead: false);
        using HttpClient client = CreateClient();
        LoginResult login = await Login(client, user);
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage logoutRequest = new(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);

        using HttpResponseMessage logoutResponse = await client.SendAsync(
            logoutRequest,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage currentResponse = await client.GetAsync(
            "/api/v1/auth/me",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, currentResponse.StatusCode);

        using HttpClient replayClient = CreateClient(handleCookies: false);
        replayClient.DefaultRequestHeaders.Add(
            "Cookie",
            login.SessionCookie[..login.SessionCookie.IndexOf(';')]);
        using HttpResponseMessage replayResponse = await replayClient.GetAsync(
            "/api/v1/auth/me",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        string[] actions = await dbContext.AuditEvents
            .Where(auditEvent => auditEvent.TenantId == tenant.TenantId)
            .OrderBy(auditEvent => auditEvent.CreatedAtUtc)
            .Select(auditEvent => auditEvent.Action)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Authentication.Login", "Authentication.Logout"], actions);
    }

    [Fact]
    public async Task LoginAndLogoutRequireAntiforgeryTokens()
    {
        TenantData tenant = await SeedTenant();
        UserData user = await SeedUser(tenant, TenantRole.Owner, createLead: false);
        using HttpClient client = CreateClient();

        using HttpResponseMessage loginWithoutToken = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(user.Email, user.Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, loginWithoutToken.StatusCode);

        _ = await Login(client, user);
        using HttpResponseMessage logoutWithoutToken = await client.PostAsync(
            "/api/v1/auth/logout",
            content: null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, logoutWithoutToken.StatusCode);
    }

    [Fact]
    public async Task LeadEndpointsRequireAuthenticationAndDenyCrossTenantAccess()
    {
        TenantData alpha = await SeedTenant();
        TenantData beta = await SeedTenant();
        UserData alphaOwner = await SeedUser(alpha, TenantRole.Owner, createLead: true);
        UserData betaOwner = await SeedUser(beta, TenantRole.Owner, createLead: true);
        Assert.NotNull(alphaOwner.LeadId);
        Assert.NotNull(betaOwner.LeadId);

        using HttpClient anonymousClient = CreateClient();
        using HttpResponseMessage anonymousResponse = await anonymousClient.GetAsync(
            "/api/v1/leads/",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using HttpClient alphaClient = CreateClient();
        _ = await Login(alphaClient, alphaOwner);
        LeadPageResponse? page = await alphaClient.GetFromJsonAsync<LeadPageResponse>(
            "/api/v1/leads/",
            TestContext.Current.CancellationToken);
        Assert.NotNull(page);
        LeadSummaryResponse onlyLead = Assert.Single(page.Items);
        Assert.Equal(alphaOwner.LeadId, onlyLead.Id);

        using HttpRequestMessage crossTenantRequest = new(
            HttpMethod.Get,
            $"/api/v1/leads/{betaOwner.LeadId}");
        crossTenantRequest.Headers.Add("X-Tenant-Id", beta.TenantId.ToString());
        using HttpResponseMessage crossTenantResponse = await alphaClient.SendAsync(
            crossTenantRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
    }

    [Fact]
    public async Task SuspendedTenantAndInvalidCredentialsUseGenericFailure()
    {
        TenantData tenant = await SeedTenant(TenantStatus.Suspended);
        UserData user = await SeedUser(tenant, TenantRole.Owner, createLead: false);
        using HttpClient client = CreateClient();

        using HttpResponseMessage suspendedResponse = await SendLogin(
            client,
            user.Email,
            user.Password);
        using HttpResponseMessage invalidResponse = await SendLogin(
            client,
            user.Email,
            $"Aa1!{Guid.CreateVersion7():N}");

        Assert.Equal(HttpStatusCode.Unauthorized, suspendedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        Assert.Contains(
            "Authentication failed",
            await suspendedResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Contains(
            "Authentication failed",
            await invalidResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipleActiveMembershipsFailClosedUntilTenantSelectionExists()
    {
        TenantData alpha = await SeedTenant();
        TenantData beta = await SeedTenant();
        UserData user = await SeedUser(alpha, TenantRole.Owner, createLead: false);
        await AddMembership(user, beta, TenantRole.Staff);
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await SendLogin(
            client,
            user.Email,
            user.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            "Authentication failed",
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardMutationsRequireCsrfApplyConcurrencyAndAuditChanges()
    {
        TenantData tenant = await SeedTenant();
        UserData owner = await SeedUser(tenant, TenantRole.Owner, createLead: true);
        UserData staff = await SeedUser(tenant, TenantRole.Staff, createLead: false);
        UserData readOnly = await SeedUser(tenant, TenantRole.ReadOnly, createLead: false);
        Guid leadId = Assert.IsType<Guid>(owner.LeadId);
        await ConfigureTenantMessaging(tenant, leadId, addPendingRecovery: true);
        using HttpClient client = CreateClient();
        _ = await Login(client, owner);

        LeadDetailResponse? initial = await client.GetFromJsonAsync<LeadDetailResponse>(
            $"/api/v1/leads/{leadId}",
            TestContext.Current.CancellationToken);
        Assert.NotNull(initial);
        Assert.Contains(
            initial.PendingActions,
            action => action.ActionType == ProcessCallStatusWebhookUseCase.RecoveryActionType);

        using HttpResponseMessage missingCsrf = await client.PostAsJsonAsync(
            $"/api/v1/leads/{leadId}/assignment",
            new AssignLeadRequest(staff.UserId, initial.Lead.RowVersion),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        using HttpResponseMessage assignedResponse = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/assignment",
            new AssignLeadRequest(staff.UserId, initial.Lead.RowVersion));
        assignedResponse.EnsureSuccessStatusCode();
        LeadDetailResponse? assigned = await assignedResponse.Content
            .ReadFromJsonAsync<LeadDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(assigned);
        Assert.Equal(staff.UserId, assigned.Lead.AssignedUserId);

        using HttpResponseMessage staleTransition = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/transitions",
            new TransitionLeadRequest(
                LeadStatus.NeedsHuman.ToString(),
                "Customer needs staff review.",
                null,
                true,
                initial.Lead.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, staleTransition.StatusCode);
        Assert.Contains(
            "changed while you were viewing",
            await staleTransition.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.OrdinalIgnoreCase);

        using HttpResponseMessage transitionedResponse = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/transitions",
            new TransitionLeadRequest(
                LeadStatus.NeedsHuman.ToString(),
                "Customer needs staff review.",
                null,
                true,
                assigned.Lead.RowVersion));
        transitionedResponse.EnsureSuccessStatusCode();
        LeadDetailResponse? transitioned = await transitionedResponse.Content
            .ReadFromJsonAsync<LeadDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(transitioned);
        Assert.Equal(LeadStatus.NeedsHuman.ToString(), transitioned.Lead.Status);

        using HttpResponseMessage pausedResponse = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/automation/pause",
            new LeadAutomationRequest(transitioned.Lead.RowVersion));
        pausedResponse.EnsureSuccessStatusCode();
        LeadDetailResponse? paused = await pausedResponse.Content
            .ReadFromJsonAsync<LeadDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(paused);
        Assert.Equal(AutomationState.PausedByUser.ToString(), paused.Lead.AutomationState);
        Assert.DoesNotContain(
            paused.PendingActions,
            action => action.ActionType == ProcessCallStatusWebhookUseCase.RecoveryActionType);

        using HttpClient readOnlyClient = CreateClient();
        _ = await Login(readOnlyClient, readOnly);
        using HttpResponseMessage forbidden = await PostWithCsrf(
            readOnlyClient,
            $"/api/v1/leads/{leadId}/notes",
            new AddLeadNoteRequest("Read-only users cannot add this note."));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        string[] auditActions = await dbContext.AuditEvents
            .Where(auditEvent =>
                auditEvent.TenantId == tenant.TenantId &&
                auditEvent.EntityId == leadId.ToString("N"))
            .Select(auditEvent => auditEvent.Action)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Contains("LeadAssigned", auditActions);
        Assert.Contains("LeadStatusChanged", auditActions);
        Assert.Contains("LeadAutomationPaused", auditActions);
    }

    [Fact]
    public async Task AiReviewIsStaffOnlyTenantScopedAuditedAndNeverSendsCustomerContent()
    {
        TenantData tenant = await SeedTenant();
        UserData owner = await SeedUser(tenant, TenantRole.Owner, createLead: true);
        UserData readOnly = await SeedUser(tenant, TenantRole.ReadOnly, createLead: false);
        Guid leadId = Assert.IsType<Guid>(owner.LeadId);
        AiAnalysisData acceptFixture = await SeedAiAnalysis(tenant, leadId, 'a');
        AiAnalysisData editFixture = await SeedAiAnalysis(tenant, leadId, 'b');
        AiAnalysisData rejectFixture = await SeedAiAnalysis(tenant, leadId, 'c');
        using HttpClient ownerClient = CreateClient();
        _ = await Login(ownerClient, owner);

        LeadDetailResponse initial = Assert.IsType<LeadDetailResponse>(
            await ownerClient.GetFromJsonAsync<LeadDetailResponse>(
                $"/api/v1/leads/{leadId}",
                TestContext.Current.CancellationToken));
        AiAnalysisReviewResponse acceptSuggestion = Assert.Single(
            initial.AiAnalyses,
            analysis => analysis.Id == acceptFixture.AnalysisId);
        AiAnalysisReviewResponse editSuggestion = Assert.Single(
            initial.AiAnalyses,
            analysis => analysis.Id == editFixture.AnalysisId);
        AiAnalysisReviewResponse rejectSuggestion = Assert.Single(
            initial.AiAnalyses,
            analysis => analysis.Id == rejectFixture.AnalysisId);
        Assert.Equal("Pending", acceptSuggestion.ReviewStatus);
        Assert.True(acceptSuggestion.RequiresHumanReview);
        Assert.True(acceptSuggestion.Confidence < 0.65);

        using HttpResponseMessage missingCsrf = await ownerClient.PostAsJsonAsync(
            $"/api/v1/leads/{leadId}/ai-analyses/{acceptFixture.AnalysisId}/accept",
            new AcceptAiAnalysisRequest(acceptSuggestion.RowVersion, null),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        using HttpClient readOnlyClient = CreateClient();
        _ = await Login(readOnlyClient, readOnly);
        using HttpResponseMessage forbidden = await PostWithCsrf(
            readOnlyClient,
            $"/api/v1/leads/{leadId}/ai-analyses/{acceptFixture.AnalysisId}/accept",
            new AcceptAiAnalysisRequest(acceptSuggestion.RowVersion, null));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        TenantData otherTenant = await SeedTenant();
        UserData otherOwner = await SeedUser(
            otherTenant,
            TenantRole.Owner,
            createLead: false);
        using HttpClient otherClient = CreateClient();
        _ = await Login(otherClient, otherOwner);
        using HttpResponseMessage crossTenant = await PostWithCsrf(
            otherClient,
            $"/api/v1/leads/{leadId}/ai-analyses/{acceptFixture.AnalysisId}/accept",
            new AcceptAiAnalysisRequest(acceptSuggestion.RowVersion, null));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        using HttpResponseMessage acceptedResponse = await PostWithCsrf(
            ownerClient,
            $"/api/v1/leads/{leadId}/ai-analyses/{acceptFixture.AnalysisId}/accept",
            new AcceptAiAnalysisRequest(acceptSuggestion.RowVersion, null));
        acceptedResponse.EnsureSuccessStatusCode();
        LeadDetailResponse accepted = Assert.IsType<LeadDetailResponse>(
            await acceptedResponse.Content.ReadFromJsonAsync<LeadDetailResponse>(
                TestContext.Current.CancellationToken));
        AiAnalysisReviewResponse acceptedAnalysis = Assert.Single(
            accepted.AiAnalyses,
            analysis => analysis.Id == acceptFixture.AnalysisId);
        Assert.Equal("Accepted", acceptedAnalysis.ReviewStatus);
        Assert.Equal(acceptedAnalysis.Suggestion, acceptedAnalysis.ReviewedValues);

        EditAiAnalysisRequest editRequest = new(
            "DrainCleaning",
            LeadUrgency.Normal.ToString(),
            "Staff confirmed that the customer needs a drain cleaning visit.",
            "Toronto",
            "M5V",
            "Tomorrow morning",
            "Thanks. Our team will review your service request.",
            "Customer clarified the requested service.",
            editSuggestion.RowVersion);
        using HttpResponseMessage editedResponse = await PostWithCsrf(
            ownerClient,
            $"/api/v1/leads/{leadId}/ai-analyses/{editFixture.AnalysisId}/edit",
            editRequest);
        editedResponse.EnsureSuccessStatusCode();
        LeadDetailResponse edited = Assert.IsType<LeadDetailResponse>(
            await editedResponse.Content.ReadFromJsonAsync<LeadDetailResponse>(
                TestContext.Current.CancellationToken));
        AiAnalysisReviewResponse editedAnalysis = Assert.Single(
            edited.AiAnalyses,
            analysis => analysis.Id == editFixture.AnalysisId);
        Assert.Equal("Edited", editedAnalysis.ReviewStatus);
        Assert.Equal("DrainCleaning", editedAnalysis.ReviewedValues?.ServiceCategory);
        Assert.Equal(editRequest.Summary, editedAnalysis.ReviewedValues?.Summary);
        Assert.Contains(edited.Timeline, item => item.Label == "AI suggestion edited");

        using HttpResponseMessage rejectedResponse = await PostWithCsrf(
            ownerClient,
            $"/api/v1/leads/{leadId}/ai-analyses/{rejectFixture.AnalysisId}/reject",
            new RejectAiAnalysisRequest(
                rejectSuggestion.RowVersion,
                "The conversation is too ambiguous."));
        rejectedResponse.EnsureSuccessStatusCode();
        LeadDetailResponse rejected = Assert.IsType<LeadDetailResponse>(
            await rejectedResponse.Content.ReadFromJsonAsync<LeadDetailResponse>(
                TestContext.Current.CancellationToken));
        AiAnalysisReviewResponse rejectedAnalysis = Assert.Single(
            rejected.AiAnalyses,
            analysis => analysis.Id == rejectFixture.AnalysisId);
        Assert.Equal("Rejected", rejectedAnalysis.ReviewStatus);
        Assert.Null(rejectedAnalysis.ReviewedValues);

        using HttpResponseMessage staleReview = await PostWithCsrf(
            ownerClient,
            $"/api/v1/leads/{leadId}/ai-analyses/{acceptFixture.AnalysisId}/accept",
            new AcceptAiAnalysisRequest(acceptSuggestion.RowVersion, null));
        Assert.Equal(HttpStatusCode.Conflict, staleReview.StatusCode);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Empty(await dbContext.Messages.IgnoreQueryFilters()
            .Where(message => message.LeadId == leadId)
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await dbContext.ScheduledActions.IgnoreQueryFilters()
            .Where(action => action.LeadId == leadId)
            .ToArrayAsync(TestContext.Current.CancellationToken));
        var audits = await dbContext.AuditEvents
            .Where(auditEvent =>
                auditEvent.TenantId == tenant.TenantId &&
                auditEvent.EntityType == nameof(AiAnalysis))
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Contains(audits, audit => audit.Action == "AiAnalysisAccepted");
        Assert.Contains(audits, audit => audit.Action == "AiAnalysisEdited");
        Assert.Contains(audits, audit => audit.Action == "AiAnalysisRejected");
        string auditJson = string.Join(
            string.Empty,
            audits.Select(audit => audit.AfterJson));
        Assert.DoesNotContain(editRequest.Summary, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Assert.IsType<string>(editRequest.SuggestedReply),
            auditJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BookingActionsAreCsrfProtectedTenantScopedCancellableAndStopWhenBooked()
    {
        TenantData tenant = await SeedTenant();
        UserData owner = await SeedUser(tenant, TenantRole.Owner, createLead: true);
        Guid leadId = Assert.IsType<Guid>(owner.LeadId);
        await ConfigureBookingWorkflow(tenant, leadId);
        using HttpClient client = CreateClient();
        _ = await Login(client, owner);
        LeadDetailResponse detail = Assert.IsType<LeadDetailResponse>(
            await client.GetFromJsonAsync<LeadDetailResponse>(
                $"/api/v1/leads/{leadId}",
                TestContext.Current.CancellationToken));
        Assert.Equal("https://booking.example.test/acme", detail.BookingUrl);

        using HttpResponseMessage missingCsrf = await client.PostAsJsonAsync(
            $"/api/v1/leads/{leadId}/booking-link",
            new LeadBookingRequest(detail.Lead.RowVersion),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        using HttpResponseMessage queuedResponse = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/booking-link",
            new LeadBookingRequest(detail.Lead.RowVersion));
        queuedResponse.EnsureSuccessStatusCode();
        LeadDetailResponse queued = Assert.IsType<LeadDetailResponse>(
            await queuedResponse.Content.ReadFromJsonAsync<LeadDetailResponse>(
                TestContext.Current.CancellationToken));
        PendingActionResponse bookingAction = Assert.Single(
            queued.PendingActions,
            action => action.ActionType == WorkflowScheduledActionTypes.SendBookingLink);
        Assert.True(bookingAction.IsCancellable);
        Assert.Equal(LeadStatus.BookingOffered.ToString(), queued.Lead.Status);

        using HttpResponseMessage repeatedQueue = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/booking-link",
            new LeadBookingRequest(queued.Lead.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, repeatedQueue.StatusCode);

        TenantData otherTenant = await SeedTenant();
        UserData otherOwner = await SeedUser(otherTenant, TenantRole.Owner, createLead: false);
        using HttpClient otherClient = CreateClient();
        _ = await Login(otherClient, otherOwner);
        using HttpResponseMessage crossTenant = await PostWithCsrf(
            otherClient,
            $"/api/v1/leads/{leadId}/scheduled-actions/{bookingAction.Id}/cancel",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        using HttpResponseMessage cancelledResponse = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/scheduled-actions/{bookingAction.Id}/cancel",
            new { });
        cancelledResponse.EnsureSuccessStatusCode();
        LeadDetailResponse cancelled = Assert.IsType<LeadDetailResponse>(
            await cancelledResponse.Content.ReadFromJsonAsync<LeadDetailResponse>(
                TestContext.Current.CancellationToken));
        Assert.DoesNotContain(cancelled.PendingActions, action => action.Id == bookingAction.Id);

        Guid followUpId = await AddPendingFollowUp(tenant, leadId);
        LeadDetailResponse beforeBooking = Assert.IsType<LeadDetailResponse>(
            await client.GetFromJsonAsync<LeadDetailResponse>(
                $"/api/v1/leads/{leadId}",
                TestContext.Current.CancellationToken));
        using HttpResponseMessage bookedResponse = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/transitions",
            new TransitionLeadRequest(
                LeadStatus.Booked.ToString(),
                "Appointment confirmed by staff.",
                null,
                true,
                beforeBooking.Lead.RowVersion));
        bookedResponse.EnsureSuccessStatusCode();
        LeadDetailResponse booked = Assert.IsType<LeadDetailResponse>(
            await bookedResponse.Content.ReadFromJsonAsync<LeadDetailResponse>(
                TestContext.Current.CancellationToken));
        Assert.Equal(LeadStatus.Booked.ToString(), booked.Lead.Status);
        Assert.DoesNotContain(booked.PendingActions, action => action.Id == followUpId);

        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Assert.Equal(
            ScheduledActionStatus.Cancelled,
            await dbContext.ScheduledActions.IgnoreQueryFilters()
                .Where(action => action.Id == followUpId)
                .Select(action => action.Status)
                .SingleAsync(TestContext.Current.CancellationToken));
        string[] auditActions = await dbContext.AuditEvents.IgnoreQueryFilters()
            .Where(auditEvent => auditEvent.TenantId == tenant.TenantId &&
                auditEvent.EntityId == leadId.ToString("N"))
            .Select(auditEvent => auditEvent.Action)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Contains("BookingLinkQueued", auditActions);
        Assert.Contains("ScheduledActionCancelled", auditActions);
        Assert.Contains("LeadStatusChanged", auditActions);
    }

    [Fact]
    public async Task ManualMessageIsIdempotentPolicyCheckedAndCompletedByWorkerFlow()
    {
        TenantData tenant = await SeedTenant();
        UserData owner = await SeedUser(tenant, TenantRole.Owner, createLead: true);
        Guid leadId = Assert.IsType<Guid>(owner.LeadId);
        await ConfigureTenantMessaging(tenant, leadId, addPendingRecovery: false);
        using HttpClient client = CreateClient();
        _ = await Login(client, owner);
        ManualMessageRequest request = new(
            "Thanks. A team member will call you shortly.",
            $"integration-{Guid.CreateVersion7():N}");

        using HttpResponseMessage firstResponse = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/messages",
            request);
        firstResponse.EnsureSuccessStatusCode();
        LeadDetailResponse? queued = await firstResponse.Content
            .ReadFromJsonAsync<LeadDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(queued);
        PendingActionResponse manualAction = Assert.Single(
            queued.PendingActions,
            action => action.ActionType == SmsScheduledActionTypes.SendManualSms);
        LeadTimelineItemResponse queuedMessage = Assert.Single(
            queued.Timeline,
            item => item.Kind == "Manual" && item.Body == request.Body);
        Assert.Equal("Queued", queuedMessage.Status);

        using HttpResponseMessage duplicateResponse = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/messages",
            request);
        duplicateResponse.EnsureSuccessStatusCode();

        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            SendScheduledManualSmsUseCase useCase =
                scope.ServiceProvider.GetRequiredService<SendScheduledManualSmsUseCase>();
            OutboundSmsOutcome outcome = await useCase.ExecuteAsync(
                manualAction.Id,
                tenant.TenantId,
                "integration-manual-worker",
                new Uri("https://webhooks.example.test/api/v1/webhooks/twilio/sms/status"),
                TestContext.Current.CancellationToken);
            Assert.Equal(OutboundSmsOutcome.Accepted, outcome);
        }

        LeadDetailResponse? sent = await client.GetFromJsonAsync<LeadDetailResponse>(
            $"/api/v1/leads/{leadId}",
            TestContext.Current.CancellationToken);
        Assert.NotNull(sent);
        LeadTimelineItemResponse sentMessage = Assert.Single(
            sent.Timeline,
            item => item.Kind == "Manual" && item.Body == request.Body);
        Assert.Equal("Sent", sentMessage.Status);

        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenant.TenantId);
            Customer customer = new(
                Guid.CreateVersion7(),
                tenant.TenantId,
                CreatePhone(tenant.TenantId),
                CreatedAtUtc);
            customer.OptOut(CreatedAtUtc.AddMinutes(1));
            dbContext.Customers.Add(customer);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using HttpResponseMessage blocked = await PostWithCsrf(
            client,
            $"/api/v1/leads/{leadId}/messages",
            new ManualMessageRequest(
                "This should be blocked.",
                $"integration-{Guid.CreateVersion7():N}"));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext verificationDb =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        int messageCount = await verificationDb.Messages
            .IgnoreQueryFilters()
            .CountAsync(
                message =>
                    message.TenantId == tenant.TenantId &&
                    message.ClientIdempotencyKey == request.IdempotencyKey,
                TestContext.Current.CancellationToken);
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public async Task TenantKillSwitchCancelsAutomationWhileInboundAndDashboardRemainAvailable()
    {
        TenantData tenant = await SeedTenant();
        UserData owner = await SeedUser(tenant, TenantRole.Owner, createLead: true);
        UserData staff = await SeedUser(tenant, TenantRole.Staff, createLead: false);
        Guid leadId = Assert.IsType<Guid>(owner.LeadId);
        await ConfigureTenantMessaging(tenant, leadId, addPendingRecovery: true);

        using HttpClient ownerClient = CreateClient();
        _ = await Login(ownerClient, owner);
        AutomationStatusResponse before = Assert.IsType<AutomationStatusResponse>(
            await ownerClient.GetFromJsonAsync<AutomationStatusResponse>(
                "/api/v1/automation/",
                TestContext.Current.CancellationToken));
        Assert.True(before.EffectiveEnabled);

        using HttpResponseMessage disabledResponse = await PostWithCsrf(
            ownerClient,
            "/api/v1/automation/tenant",
            new SetTenantAutomationRequest(
                false,
                before.TenantRowVersion,
                AutomationControlReason.OperationalIncident.ToString()));
        disabledResponse.EnsureSuccessStatusCode();
        AutomationStatusResponse disabled = Assert.IsType<AutomationStatusResponse>(
            await disabledResponse.Content.ReadFromJsonAsync<AutomationStatusResponse>(
                TestContext.Current.CancellationToken));
        Assert.False(disabled.TenantEnabled);
        Assert.False(disabled.EffectiveEnabled);
        Assert.Equal(1, disabled.CancelledActionCount);

        using HttpResponseMessage staleResponse = await PostWithCsrf(
            ownerClient,
            "/api/v1/automation/tenant",
            new SetTenantAutomationRequest(
                true,
                before.TenantRowVersion,
                AutomationControlReason.IncidentResolved.ToString()));
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        using HttpClient staffClient = CreateClient();
        _ = await Login(staffClient, staff);
        AutomationStatusResponse staffStatus = Assert.IsType<AutomationStatusResponse>(
            await staffClient.GetFromJsonAsync<AutomationStatusResponse>(
                "/api/v1/automation/",
                TestContext.Current.CancellationToken));
        Assert.False(staffStatus.EffectiveEnabled);
        using HttpResponseMessage forbidden = await PostWithCsrf(
            staffClient,
            "/api/v1/automation/tenant",
            new SetTenantAutomationRequest(
                true,
                staffStatus.TenantRowVersion,
                AutomationControlReason.TenantRequest.ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        string providerPhone;
        string customerPhone;
        await using (AsyncServiceScope scope =
            fixture.Application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            using TenantClaimScope tenantClaim =
                new(scope.ServiceProvider, tenant.TenantId);
            providerPhone = await dbContext.TenantPhoneNumbers
                .Select(number => number.PhoneNumberE164)
                .SingleAsync(TestContext.Current.CancellationToken);
            customerPhone = await dbContext.Leads
                .Where(lead => lead.Id == leadId)
                .Select(lead => lead.PrimaryPhoneE164)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        await using (AsyncServiceScope scope =
            fixture.Application.Services.CreateAsyncScope())
        {
            ProcessInboundSmsUseCase inbound =
                scope.ServiceProvider.GetRequiredService<ProcessInboundSmsUseCase>();
            string providerSid = $"SM{Guid.CreateVersion7():N}";
            InboundSmsOutcome outcome = await inbound.ExecuteAsync(
                new InboundSmsWebhookEvent(
                    "Twilio",
                    providerSid,
                    customerPhone,
                    providerPhone,
                    "Please call me while automation is paused.",
                    $"twilio:message:{providerSid}",
                    new string('a', 64),
                    $"integration-{Guid.CreateVersion7():N}"),
                TestContext.Current.CancellationToken);
            Assert.Equal(InboundSmsOutcome.Received, outcome);
        }

        LeadDetailResponse detail = Assert.IsType<LeadDetailResponse>(
            await ownerClient.GetFromJsonAsync<LeadDetailResponse>(
                $"/api/v1/leads/{leadId}",
                TestContext.Current.CancellationToken));
        Assert.Contains(
            detail.Timeline,
            item => item.Body == "Please call me while automation is paused.");

        await using (AsyncServiceScope scope =
            fixture.Application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            Assert.Equal(
                ScheduledActionStatus.Cancelled,
                await dbContext.ScheduledActions.IgnoreQueryFilters()
                    .Where(action =>
                        action.TenantId == tenant.TenantId &&
                        action.ActionType ==
                            ProcessCallStatusWebhookUseCase.RecoveryActionType)
                    .Select(action => action.Status)
                    .SingleAsync(TestContext.Current.CancellationToken));
            Assert.Contains(
                await dbContext.AuditEvents.IgnoreQueryFilters()
                    .Where(auditEvent => auditEvent.TenantId == tenant.TenantId)
                    .Select(auditEvent => auditEvent.Action)
                    .ToArrayAsync(TestContext.Current.CancellationToken),
                action => action == "TenantAutomationDisabled");
        }

        using HttpResponseMessage enabledResponse = await PostWithCsrf(
            ownerClient,
            "/api/v1/automation/tenant",
            new SetTenantAutomationRequest(
                true,
                disabled.TenantRowVersion,
                AutomationControlReason.IncidentResolved.ToString()));
        enabledResponse.EnsureSuccessStatusCode();
        AutomationStatusResponse enabled = Assert.IsType<AutomationStatusResponse>(
            await enabledResponse.Content.ReadFromJsonAsync<AutomationStatusResponse>(
                TestContext.Current.CancellationToken));
        Assert.True(enabled.EffectiveEnabled);
    }

    [Fact]
    public async Task GlobalKillSwitchCancelsAutomationButPreservesManualActions()
    {
        TenantData tenant = await SeedTenant();
        UserData owner = await SeedUser(tenant, TenantRole.Owner, createLead: true);
        Guid leadId = Assert.IsType<Guid>(owner.LeadId);
        await ConfigureTenantMessaging(tenant, leadId, addPendingRecovery: true);
        Guid manualActionId = Guid.CreateVersion7();
        await using (AsyncServiceScope scope =
            fixture.Application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            using TenantClaimScope tenantClaim =
                new(scope.ServiceProvider, tenant.TenantId);
            dbContext.ScheduledActions.Add(new ScheduledAction(
                manualActionId,
                tenant.TenantId,
                leadId,
                SmsScheduledActionTypes.SendManualSms,
                DateTimeOffset.UtcNow.AddMinutes(10),
                $"manual-preserved:{manualActionId:N}",
                "{}",
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using WebApplicationFactory<Program> globallyDisabled =
            fixture.Application.WithWebHostBuilder(builder =>
                builder.UseSetting("AUTOMATION_GLOBAL_ENABLED", "false"));
        await using (AsyncServiceScope scope =
            globallyDisabled.Services.CreateAsyncScope())
        {
            AutomationControlUseCase control =
                scope.ServiceProvider.GetRequiredService<AutomationControlUseCase>();
            GlobalAutomationEnforcementResult result =
                await control.EnforceGlobalDisableAsync(
                    TestContext.Current.CancellationToken);
            Assert.False(result.GlobalEnabled);
            Assert.True(result.CancelledActionCount >= 1);
        }

        await using AsyncServiceScope verificationScope =
            fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext verificationDb =
            verificationScope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Dictionary<string, ScheduledActionStatus> statuses =
            await verificationDb.ScheduledActions.IgnoreQueryFilters()
                .Where(action =>
                    action.TenantId == tenant.TenantId &&
                    (action.ActionType ==
                        ProcessCallStatusWebhookUseCase.RecoveryActionType ||
                        action.Id == manualActionId))
                .ToDictionaryAsync(
                    action => action.ActionType,
                    action => action.Status,
                    TestContext.Current.CancellationToken);
        Assert.Equal(
            ScheduledActionStatus.Cancelled,
            statuses[ProcessCallStatusWebhookUseCase.RecoveryActionType]);
        Assert.Equal(
            ScheduledActionStatus.Pending,
            statuses[SmsScheduledActionTypes.SendManualSms]);
    }

    [Fact]
    public async Task LeadInboxMeetsPilotTargetWithTenThousandTenantLeads()
    {
        TenantData tenant = await SeedTenant();
        UserData owner = await SeedUser(tenant, TenantRole.Owner, createLead: false);
        await using (AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope())
        {
            LeadRecoveryDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into leads
                    (id, tenant_id, primary_phone_e164, source, status, urgency,
                     automation_state, version, created_at_utc, updated_at_utc)
                select gen_random_uuid(), {tenant.TenantId}, '+14165550100',
                       'Import', 'New', 'Unknown', 'Active', 0,
                       {CreatedAtUtc} + (value * interval '1 second'),
                       {CreatedAtUtc} + (value * interval '1 second')
                from generate_series(1, 10000) as value
                """,
                TestContext.Current.CancellationToken);
        }

        using HttpClient client = CreateClient();
        _ = await Login(client, owner);
        const string path =
            "/api/v1/leads/?pageSize=100&status=New&urgency=Unknown&assignment=unassigned";
        using (HttpResponseMessage warmup = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken))
        {
            warmup.EnsureSuccessStatusCode();
        }

        List<TimeSpan> durations = [];
        for (int iteration = 0; iteration < 10; iteration++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            using HttpResponseMessage response = await client.GetAsync(
                path,
                TestContext.Current.CancellationToken);
            stopwatch.Stop();
            response.EnsureSuccessStatusCode();
            durations.Add(stopwatch.Elapsed);
        }

        TimeSpan p95 = durations.Order().ElementAt(9);
        Assert.True(
            p95 < TimeSpan.FromMilliseconds(500),
            $"Expected dashboard p95 below 500 ms, measured {p95.TotalMilliseconds:F1} ms.");
    }

    [Fact]
    public async Task OwnerPolicyAllowsOwnerAndDeniesStaff()
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        IAuthorizationService authorization =
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal owner = CreatePrincipal(TenantRole.Owner);
        ClaimsPrincipal staff = CreatePrincipal(TenantRole.Staff);

        AuthorizationResult ownerResult = await authorization.AuthorizeAsync(
            owner,
            resource: null,
            AuthorizationPolicies.OwnerOnly);
        AuthorizationResult staffResult = await authorization.AuthorizeAsync(
            staff,
            resource: null,
            AuthorizationPolicies.OwnerOnly);

        Assert.True(ownerResult.Succeeded);
        Assert.False(staffResult.Succeeded);
    }

    private HttpClient CreateClient(bool handleCookies = true) =>
        fixture.Application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
        });

    private async Task ConfigureTenantMessaging(
        TenantData tenantData,
        Guid leadId,
        bool addPendingRecovery)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        using TenantClaimScope tenantClaim =
            new(scope.ServiceProvider, tenantData.TenantId);
        Tenant tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == tenantData.TenantId,
            TestContext.Current.CancellationToken);
        tenant.SetAutomationEnabled(true, CreatedAtUtc.AddMinutes(1));
        dbContext.TenantPhoneNumbers.Add(new TenantPhoneNumber(
            Guid.CreateVersion7(),
            tenantData.TenantId,
            "Twilio",
            CreatePhone(Guid.CreateVersion7()),
            $"PN{Guid.CreateVersion7():N}",
            ["busy", "failed", "no-answer"],
            initialDelaySeconds: 60,
            recoveryCooldownSeconds: 3600,
            isPrimary: true));
        if (addPendingRecovery)
        {
            dbContext.ScheduledActions.Add(new ScheduledAction(
                Guid.CreateVersion7(),
                tenantData.TenantId,
                leadId,
                ProcessCallStatusWebhookUseCase.RecoveryActionType,
                CreatedAtUtc.AddHours(1),
                $"integration-recovery:{leadId:N}",
                """{"schemaVersion":1}""",
                CreatedAtUtc.AddMinutes(1)));
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ConfigureBookingWorkflow(TenantData tenantData, Guid leadId)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantData.TenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Tenant tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == tenantData.TenantId,
            TestContext.Current.CancellationToken);
        tenant.SetAutomationEnabled(true, CreatedAtUtc.AddMinutes(1));
        Lead lead = await dbContext.Leads.SingleAsync(
            candidate => candidate.Id == leadId,
            TestContext.Current.CancellationToken);
        lead.BeginContacting(CreatedAtUtc.AddMinutes(1));
        lead.AwaitCustomer(CreatedAtUtc.AddMinutes(1));
        lead.Qualify(true, null, CreatedAtUtc.AddMinutes(1));
        WorkflowDefinition workflow = new(
            Guid.CreateVersion7(),
            tenantData.TenantId,
            "Booking workflow",
            1,
            "https://booking.example.test/acme",
            [
                new QualificationQuestionPolicy(
                    "service",
                    "Which service do you need?",
                    QualificationAnswerKind.RequiredText,
                    []),
            ],
            new BusinessHoursPolicy(
                Enum.GetValues<DayOfWeek>()
                    .Select(day => new BusinessDayHours(
                        day,
                        new TimeOnly(0, 0),
                        new TimeOnly(23, 59)))
                    .ToArray(),
                true),
            [new FollowUpStepPolicy(1, 30, "BookingFollowUp")],
            CreatedAtUtc);
        workflow.Activate(CreatedAtUtc.AddMinutes(1));
        dbContext.WorkflowDefinitions.Add(workflow);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> AddPendingFollowUp(TenantData tenantData, Guid leadId)
    {
        Guid actionId = Guid.CreateVersion7();
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenantData.TenantId);
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        dbContext.ScheduledActions.Add(new ScheduledAction(
            actionId,
            tenantData.TenantId,
            leadId,
            WorkflowScheduledActionTypes.SendFollowUpSms,
            DateTimeOffset.UtcNow.AddMinutes(30),
            $"booking-followup:{actionId:N}",
            WorkflowScheduledActionPayloadSerializer.Serialize(new(
                1,
                "booking:BookingOffered",
                null,
                1,
                "BookingFollowUp",
                null)),
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return actionId;
    }

    private async Task<TenantData> SeedTenant(
        TenantStatus status = TenantStatus.Trial)
    {
        Guid tenantId = Guid.CreateVersion7();
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        Tenant tenant = new(
            tenantId,
            $"Tenant {tenantId:N}",
            $"tenant-{tenantId:N}",
            "America/Toronto",
            CreatedAtUtc);
        if (status != TenantStatus.Trial)
        {
            tenant.ChangeStatus(status, CreatedAtUtc.AddSeconds(1));
        }

        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new TenantData(tenantId);
    }

    private async Task<AiAnalysisData> SeedAiAnalysis(
        TenantData tenant,
        Guid leadId,
        char hashCharacter)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenant.TenantId);
        AiAnalysis analysis = new(
            Guid.CreateVersion7(),
            tenant.TenantId,
            leadId,
            "1.0",
            "Test",
            "fictional-integration-fixture",
            new string(hashCharacter, AiAnalysisFieldLimits.InputHashLength),
            ["LeakRepair", "DrainCleaning"],
            new AiAnalysisValues(
                "LeakRepair",
                LeadUrgency.High,
                "Customer reports an active leak and requests a callback.",
                "Mississauga",
                null,
                "As soon as possible",
                "Thanks. A team member will review this and contact you shortly."),
            0.58,
            requiresHumanReview: true,
            ["ACTIVE_PROPERTY_DAMAGE"],
            CreatedAtUtc.AddMinutes(1));
        dbContext.AiAnalyses.Add(analysis);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new AiAnalysisData(analysis.Id);
    }

    private async Task<UserData> SeedUser(
        TenantData tenant,
        TenantRole role,
        bool createLead)
    {
        Guid userId = Guid.CreateVersion7();
        string email = $"{userId:N}@example.test";
        string password = $"Aa1!{Guid.CreateVersion7():N}";
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        UserManager<ApplicationUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        ApplicationUser user = new(userId, email, $"Test {role}", CreatedAtUtc)
        {
            EmailConfirmed = true,
        };
        IdentityResult creation = await userManager.CreateAsync(user, password);
        Assert.True(
            creation.Succeeded,
            string.Join(", ", creation.Errors.Select(error => error.Code)));

        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenant.TenantId);
        dbContext.TenantMemberships.Add(new TenantMembership(
            Guid.CreateVersion7(),
            tenant.TenantId,
            userId,
            role,
            CreatedAtUtc));
        Guid? leadId = null;
        if (createLead)
        {
            leadId = Guid.CreateVersion7();
            dbContext.Leads.Add(new Lead(
                leadId.Value,
                tenant.TenantId,
                CreatePhone(tenant.TenantId),
                LeadSource.Manual,
                CreatedAtUtc,
                $"Lead {tenant.TenantId:N}"));
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new UserData(userId, email, password, leadId);
    }

    private async Task AddMembership(
        UserData user,
        TenantData tenant,
        TenantRole role)
    {
        await using AsyncServiceScope scope = fixture.Application.Services.CreateAsyncScope();
        LeadRecoveryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<LeadRecoveryDbContext>();
        using TenantClaimScope tenantClaim = new(scope.ServiceProvider, tenant.TenantId);
        dbContext.TenantMemberships.Add(new TenantMembership(
            Guid.CreateVersion7(),
            tenant.TenantId,
            user.UserId,
            role,
            CreatedAtUtc));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<LoginResult> Login(HttpClient client, UserData user)
    {
        using HttpResponseMessage response = await SendLogin(
            client,
            user.Email,
            user.Password);
        response.EnsureSuccessStatusCode();
        AuthSessionResponse? session = await response.Content
            .ReadFromJsonAsync<AuthSessionResponse>(TestContext.Current.CancellationToken);
        string sessionCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                "leadrecovery.session=",
                StringComparison.OrdinalIgnoreCase));
        return new LoginResult(Assert.IsType<AuthSessionResponse>(session), sessionCookie);
    }

    private static async Task<HttpResponseMessage> SendLogin(
        HttpClient client,
        string email,
        string password)
    {
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password)),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostWithCsrf<TRequest>(
        HttpClient client,
        string path,
        TRequest requestBody)
    {
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetCsrfToken(HttpClient client)
    {
        CsrfTokenResponse? response = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/api/v1/auth/csrf",
            TestContext.Current.CancellationToken);
        return Assert.IsType<CsrfTokenResponse>(response).Token;
    }

    private static ClaimsPrincipal CreatePrincipal(TenantRole role)
    {
        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
                new Claim(TenantClaimTypes.TenantId, Guid.CreateVersion7().ToString()),
                new Claim(ClaimTypes.Role, role.ToString()),
            ],
            "TestCookie");
        return new ClaimsPrincipal(identity);
    }

    private static string CreatePhone(Guid tenantId)
    {
        byte[] bytes = tenantId.ToByteArray();
        ulong suffix = BitConverter.ToUInt64(bytes, 0) % 10_000_000_000UL;
        return $"+1{suffix:D10}";
    }

    private sealed record TenantData(Guid TenantId);

    private sealed record AiAnalysisData(Guid AnalysisId);

    private sealed record UserData(
        Guid UserId,
        string Email,
        string Password,
        Guid? LeadId);

    private sealed record LoginResult(
        AuthSessionResponse Session,
        string SessionCookie);

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
