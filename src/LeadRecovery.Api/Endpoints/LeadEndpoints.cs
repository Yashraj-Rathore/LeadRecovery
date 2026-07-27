using System.Buffers.Binary;
using System.Security.Claims;

using LeadRecovery.Application.Authorization;
using LeadRecovery.Application.Leads;
using LeadRecovery.Contracts.Leads;
using LeadRecovery.Domain.Analysis;
using LeadRecovery.Domain.Leads;

using Microsoft.AspNetCore.Antiforgery;

namespace LeadRecovery.Api.Endpoints;

internal static class LeadEndpoints
{
    public static IEndpointRouteBuilder MapLeadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder leads = endpoints.MapGroup("/api/v1/leads")
            .WithTags("Leads")
            .RequireAuthorization(AuthorizationPolicies.TenantMember);

        leads.MapGet(
            "/",
            async Task<IResult> (
                int? pageSize,
                string? cursor,
                string? status,
                string? urgency,
                string? assignment,
                Guid? assignedUserId,
                HttpContext context,
                ListLeadsUseCase useCase,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    if (!TryGetUserId(context.User, out Guid currentUserId))
                    {
                        return Results.Unauthorized();
                    }

                    LeadInboxCriteria criteria = new(
                        ParseOptionalEnum<LeadStatus>(status, nameof(status)),
                        ParseOptionalEnum<LeadUrgency>(urgency, nameof(urgency)),
                        ParseAssignment(assignment),
                        currentUserId,
                        assignedUserId);
                    LeadInboxPage page = await useCase.ExecuteAsync(
                        pageSize ?? 25,
                        cursor,
                        criteria,
                        cancellationToken);
                    return Results.Ok(new LeadPageResponse(
                        page.Items.Select(Map).ToArray(),
                        page.NextCursor));
                }
                catch (ArgumentException exception)
                {
                    return Validation(exception);
                }
            });

        leads.MapGet(
            "/assignees",
            async Task<IResult> (
                LeadDashboardUseCase useCase,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AssignableUserItem> users =
                    await useCase.ListAssignableUsersAsync(cancellationToken);
                return Results.Ok(users.Select(Map).ToArray());
            });

        leads.MapGet(
            "/{leadId:guid}",
            async Task<IResult> (
                Guid leadId,
                LeadDashboardUseCase useCase,
                CancellationToken cancellationToken) =>
            {
                LeadDetail? lead = await useCase.GetDetailAsync(
                    leadId,
                    cancellationToken);
                return lead is null ? Results.NotFound() : Results.Ok(Map(lead));
            });

        leads.MapGet(
            "/{leadId:guid}/messages",
            async Task<IResult> (
                Guid leadId,
                LeadDashboardUseCase useCase,
                CancellationToken cancellationToken) =>
            {
                LeadDetail? lead = await useCase.GetDetailAsync(
                    leadId,
                    cancellationToken);
                return lead is null
                    ? Results.NotFound()
                    : Results.Ok(
                        lead.Timeline
                            .Where(item => item.Type == "Sms")
                            .Select(Map)
                            .ToArray());
            });

        leads.MapPost(
                "/{leadId:guid}/assignment",
                async Task<IResult> (
                    Guid leadId,
                    AssignLeadRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                    await Mutate(
                        context,
                        antiforgery,
                        leadId,
                        request.ExpectedRowVersion,
                        useCase,
                        (actorId, version) => useCase.AssignAsync(
                            leadId,
                            request.AssignedUserId,
                            version,
                            actorId,
                            context.TraceIdentifier,
                            cancellationToken),
                        cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/transitions",
                async Task<IResult> (
                    Guid leadId,
                    TransitionLeadRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                {
                    if (!Enum.TryParse(
                            request.TargetStatus,
                            ignoreCase: true,
                            out LeadStatus targetStatus) ||
                        !Enum.IsDefined(targetStatus))
                    {
                        return Validation("targetStatus", "The target status is invalid.");
                    }

                    LeadCloseReason? closeReason = null;
                    if (!string.IsNullOrWhiteSpace(request.CloseReason))
                    {
                        if (!Enum.TryParse(
                                request.CloseReason,
                                ignoreCase: true,
                                out LeadCloseReason parsedReason) ||
                            !Enum.IsDefined(parsedReason))
                        {
                            return Validation("closeReason", "The close reason is invalid.");
                        }

                        closeReason = parsedReason;
                    }

                    LeadTransitionCommand command = new(
                        targetStatus,
                        request.Reason,
                        closeReason,
                        request.MinimumRequiredDetailsPresent);
                    return await Mutate(
                        context,
                        antiforgery,
                        leadId,
                        request.ExpectedRowVersion,
                        useCase,
                        (actorId, version) => useCase.TransitionAsync(
                            leadId,
                            command,
                            version,
                            actorId,
                            context.TraceIdentifier,
                            cancellationToken),
                        cancellationToken);
                })
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/automation/pause",
                async Task<IResult> (
                    Guid leadId,
                    LeadAutomationRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                    await Mutate(
                        context,
                        antiforgery,
                        leadId,
                        request.ExpectedRowVersion,
                        useCase,
                        (actorId, version) => useCase.SetAutomationPausedAsync(
                            leadId,
                            paused: true,
                            version,
                            actorId,
                            context.TraceIdentifier,
                            cancellationToken),
                        cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/automation/resume",
                async Task<IResult> (
                    Guid leadId,
                    LeadAutomationRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                    await Mutate(
                        context,
                        antiforgery,
                        leadId,
                        request.ExpectedRowVersion,
                        useCase,
                        (actorId, version) => useCase.SetAutomationPausedAsync(
                            leadId,
                            paused: false,
                            version,
                            actorId,
                            context.TraceIdentifier,
                            cancellationToken),
                        cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/notes",
                async Task<IResult> (
                    Guid leadId,
                    AddLeadNoteRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                {
                    if (!await IsAntiforgeryRequestValid(antiforgery, context))
                    {
                        return AntiforgeryFailure();
                    }

                    if (!TryGetUserId(context.User, out Guid actorId))
                    {
                        return Results.Unauthorized();
                    }

                    try
                    {
                        LeadOperationResult result = await useCase.AddNoteAsync(
                            leadId,
                            request.Body,
                            actorId,
                            context.TraceIdentifier,
                            cancellationToken);
                        return await MapOperation(
                            result,
                            leadId,
                            useCase,
                            cancellationToken);
                    }
                    catch (ArgumentException exception)
                    {
                        return Validation(exception);
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/messages",
                async Task<IResult> (
                    Guid leadId,
                    ManualMessageRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                {
                    if (!await IsAntiforgeryRequestValid(antiforgery, context))
                    {
                        return AntiforgeryFailure();
                    }

                    if (!TryGetUserId(context.User, out Guid actorId))
                    {
                        return Results.Unauthorized();
                    }

                    try
                    {
                        LeadOperationResult result =
                            await useCase.QueueManualMessageAsync(
                                leadId,
                                new QueueManualMessageCommand(
                                    request.Body,
                                    request.IdempotencyKey),
                                actorId,
                                context.TraceIdentifier,
                                cancellationToken);
                        return await MapOperation(
                            result,
                            leadId,
                            useCase,
                            cancellationToken);
                    }
                    catch (ArgumentException exception)
                    {
                        return Validation(exception);
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator)
            .RequireRateLimiting("manual-message");

        leads.MapPost(
                "/{leadId:guid}/booking-link",
                async Task<IResult> (
                    Guid leadId,
                    LeadBookingRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                    await Mutate(
                        context,
                        antiforgery,
                        leadId,
                        request.ExpectedRowVersion,
                        useCase,
                        (actorId, version) => useCase.QueueBookingLinkAsync(
                            leadId,
                            version,
                            actorId,
                            context.TraceIdentifier,
                            cancellationToken),
                        cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/scheduled-actions/{actionId:guid}/cancel",
                async Task<IResult> (
                    Guid leadId,
                    Guid actionId,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                {
                    if (!await IsAntiforgeryRequestValid(antiforgery, context))
                    {
                        return AntiforgeryFailure();
                    }

                    if (!TryGetUserId(context.User, out Guid actorId))
                    {
                        return Results.Unauthorized();
                    }

                    try
                    {
                        LeadOperationResult result =
                            await useCase.CancelScheduledActionAsync(
                                leadId,
                                actionId,
                                actorId,
                                context.TraceIdentifier,
                                cancellationToken);
                        return await MapOperation(
                            result,
                            leadId,
                            useCase,
                            cancellationToken);
                    }
                    catch (ArgumentException exception)
                    {
                        return Validation(exception);
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/ai-analyses/{analysisId:guid}/accept",
                async Task<IResult> (
                    Guid leadId,
                    Guid analysisId,
                    AcceptAiAnalysisRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                    await MutateAnalysis(
                        context,
                        antiforgery,
                        leadId,
                        analysisId,
                        request.ExpectedRowVersion,
                        new ReviewLeadAnalysisCommand(
                            LeadAnalysisReviewAction.Accept,
                            null,
                            request.CorrectionReason),
                        useCase,
                        cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/ai-analyses/{analysisId:guid}/edit",
                async Task<IResult> (
                    Guid leadId,
                    Guid analysisId,
                    EditAiAnalysisRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                {
                    if (!Enum.TryParse(
                            request.Urgency,
                            ignoreCase: true,
                            out LeadUrgency urgency) ||
                        !Enum.IsDefined(urgency))
                    {
                        return Validation("urgency", "The urgency value is invalid.");
                    }

                    AiAnalysisValues values = new(
                        request.ServiceCategory,
                        urgency,
                        request.Summary,
                        request.City,
                        request.PostalCode,
                        request.PreferredCallbackWindow,
                        request.SuggestedReply);
                    return await MutateAnalysis(
                        context,
                        antiforgery,
                        leadId,
                        analysisId,
                        request.ExpectedRowVersion,
                        new ReviewLeadAnalysisCommand(
                            LeadAnalysisReviewAction.Edit,
                            values,
                            request.CorrectionReason),
                        useCase,
                        cancellationToken);
                })
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        leads.MapPost(
                "/{leadId:guid}/ai-analyses/{analysisId:guid}/reject",
                async Task<IResult> (
                    Guid leadId,
                    Guid analysisId,
                    RejectAiAnalysisRequest request,
                    HttpContext context,
                    IAntiforgery antiforgery,
                    LeadDashboardUseCase useCase,
                    CancellationToken cancellationToken) =>
                    await MutateAnalysis(
                        context,
                        antiforgery,
                        leadId,
                        analysisId,
                        request.ExpectedRowVersion,
                        new ReviewLeadAnalysisCommand(
                            LeadAnalysisReviewAction.Reject,
                            null,
                            request.CorrectionReason),
                        useCase,
                        cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.DashboardOperator);

        return endpoints;
    }

    private static async Task<IResult> MutateAnalysis(
        HttpContext context,
        IAntiforgery antiforgery,
        Guid leadId,
        Guid analysisId,
        string expectedRowVersion,
        ReviewLeadAnalysisCommand command,
        LeadDashboardUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!await IsAntiforgeryRequestValid(antiforgery, context))
        {
            return AntiforgeryFailure();
        }

        if (!TryGetUserId(context.User, out Guid actorId))
        {
            return Results.Unauthorized();
        }

        if (!TryDecodeVersion(expectedRowVersion, out long expectedVersion))
        {
            return Validation(
                "expectedRowVersion",
                "The expected row version is invalid.");
        }

        try
        {
            LeadOperationResult result = await useCase.ReviewAnalysisAsync(
                leadId,
                analysisId,
                command,
                expectedVersion,
                actorId,
                context.TraceIdentifier,
                cancellationToken);
            return await MapOperation(result, leadId, useCase, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> Mutate(
        HttpContext context,
        IAntiforgery antiforgery,
        Guid leadId,
        string expectedRowVersion,
        LeadDashboardUseCase useCase,
        Func<Guid, long, Task<LeadOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!await IsAntiforgeryRequestValid(antiforgery, context))
        {
            return AntiforgeryFailure();
        }

        if (!TryGetUserId(context.User, out Guid actorId))
        {
            return Results.Unauthorized();
        }

        if (!TryDecodeVersion(expectedRowVersion, out long expectedVersion))
        {
            return Validation(
                "expectedRowVersion",
                "The expected row version is invalid.");
        }

        try
        {
            LeadOperationResult result = await operation(actorId, expectedVersion);
            return await MapOperation(result, leadId, useCase, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> MapOperation(
        LeadOperationResult result,
        Guid leadId,
        LeadDashboardUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (result.Status == LeadOperationStatus.NotFound)
        {
            return Results.NotFound();
        }

        if (result.Status == LeadOperationStatus.Invalid)
        {
            return Validation("request", result.Detail ?? "The request is invalid.");
        }

        LeadDetail? current = await useCase.GetDetailAsync(leadId, cancellationToken);
        if (result.Status == LeadOperationStatus.Conflict)
        {
            return Results.Json(
                new
                {
                    type = "https://leadrecovery.example/errors/concurrency",
                    title = "The lead changed while you were viewing it.",
                    status = StatusCodes.Status409Conflict,
                    detail = "Review the latest lead state before trying again.",
                    current = current is null ? null : Map(current),
                },
                statusCode: StatusCodes.Status409Conflict,
                contentType: "application/problem+json");
        }

        if (result.Status == LeadOperationStatus.PolicyBlocked)
        {
            return Results.Json(
                new
                {
                    type = "https://leadrecovery.example/errors/policy-blocked",
                    title = "The action is blocked by policy.",
                    status = StatusCodes.Status409Conflict,
                    detail = result.Detail,
                    current = current is null ? null : Map(current),
                },
                statusCode: StatusCodes.Status409Conflict,
                contentType: "application/problem+json");
        }

        return current is null ? Results.NotFound() : Results.Ok(Map(current));
    }

    private static LeadSummaryResponse Map(LeadInboxItem lead) =>
        new(
            lead.Id,
            lead.DisplayName,
            lead.PrimaryPhoneE164,
            lead.Source.ToString(),
            lead.Status.ToString(),
            lead.Urgency.ToString(),
            lead.AutomationState.ToString(),
            lead.AssignedUserId,
            lead.AssignedUserName,
            lead.LastActivityAtUtc,
            lead.HasUnreadCustomerActivity,
            EncodeVersion(lead.Version),
            lead.CreatedAtUtc);

    private static LeadDetailResponse Map(LeadDetail detail) =>
        new(
            Map(detail.Lead),
            detail.Timeline.Select(Map).ToArray(),
            detail.PendingActions.Select(action => new PendingActionResponse(
                action.Id,
                action.ActionType,
                action.Status.ToString(),
                action.ScheduledForUtc,
                action.AttemptCount,
                action.IsCancellable)).ToArray(),
            detail.AssignableUsers.Select(Map).ToArray(),
            detail.AllowedTransitions.Select(status => status.ToString()).ToArray(),
            detail.QualificationAnswers.Select(answer => new QualificationAnswerResponse(
                answer.Id,
                answer.QuestionKey,
                answer.QuestionPrompt,
                answer.Value,
                answer.Outcome,
                answer.CreatedAtUtc)).ToArray(),
            detail.CurrentQualificationQuestion,
            detail.BookingUrl,
            detail.AiAnalyses.Select(analysis => new AiAnalysisReviewResponse(
                analysis.Id,
                analysis.SchemaVersion,
                analysis.AllowedCategories,
                Map(analysis.Suggestion),
                analysis.Confidence,
                analysis.RequiresHumanReview,
                analysis.ReasonCodes,
                analysis.ReviewStatus.ToString(),
                analysis.ReviewedValues is null
                    ? null
                    : Map(analysis.ReviewedValues),
                analysis.CorrectionReason,
                analysis.ReviewedByUserId,
                analysis.ReviewedByUserName,
                analysis.ReviewedAtUtc,
                EncodeVersion(analysis.Version),
                analysis.CreatedAtUtc)).ToArray());

    private static AiAnalysisValuesResponse Map(AiAnalysisValues values) =>
        new(
            values.ServiceCategory,
            values.Urgency.ToString(),
            values.Summary,
            values.City,
            values.PostalCode,
            values.PreferredCallbackWindow,
            values.SuggestedReply);

    private static LeadTimelineItemResponse Map(LeadTimelineItem item) =>
        new(
            item.Id,
            item.Type,
            item.Label,
            item.Body,
            item.Direction,
            item.Kind,
            item.Status,
            item.FailureDescription,
            item.ActorName,
            item.OccurredAtUtc);

    private static AssignableUserResponse Map(AssignableUserItem user) =>
        new(user.UserId, user.DisplayName, user.Role);

    private static LeadAssignmentFilter ParseAssignment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return LeadAssignmentFilter.All;
        }

        if (value.Equals("unassigned", StringComparison.OrdinalIgnoreCase))
        {
            return LeadAssignmentFilter.Unassigned;
        }

        if (value.Equals("mine", StringComparison.OrdinalIgnoreCase))
        {
            return LeadAssignmentFilter.Mine;
        }

        throw new ArgumentException("The assignment filter is invalid.", nameof(value));
    }

    private static TEnum? ParseOptionalEnum<TEnum>(string? value, string parameterName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out TEnum result) ||
            !Enum.IsDefined(result))
        {
            throw new ArgumentException("The filter value is invalid.", parameterName);
        }

        return result;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out userId) &&
        userId != Guid.Empty;

    private static string EncodeVersion(long version)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, version);
        return Convert.ToBase64String(bytes);
    }

    private static bool TryDecodeVersion(string? token, out long version)
    {
        version = -1;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(token);
            if (bytes.Length != sizeof(long))
            {
                return false;
            }

            version = BinaryPrimitives.ReadInt64BigEndian(bytes);
            return version >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<bool> IsAntiforgeryRequestValid(
        IAntiforgery antiforgery,
        HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static IResult AntiforgeryFailure() => Results.Problem(
        title: "Antiforgery validation failed",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult Validation(ArgumentException exception) =>
        Validation(exception.ParamName ?? "request", exception.Message);

    private static IResult Validation(string field, string detail) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [detail],
        });
}
