using LeadRecovery.Application.Authorization;
using LeadRecovery.Application.Leads;
using LeadRecovery.Contracts.Leads;

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
                ListLeadsUseCase useCase,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    LeadInboxPage page = await useCase.ExecuteAsync(
                        pageSize ?? 25,
                        cursor,
                        cancellationToken);
                    return Results.Ok(new LeadPageResponse(
                        page.Items.Select(Map).ToArray(),
                        page.NextCursor));
                }
                catch (ArgumentException exception)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [exception.ParamName ?? "request"] = [exception.Message],
                    });
                }
            });

        leads.MapGet(
            "/{leadId:guid}",
            async Task<IResult> (
                Guid leadId,
                GetLeadUseCase useCase,
                CancellationToken cancellationToken) =>
            {
                LeadInboxItem? lead = await useCase.ExecuteAsync(
                    leadId,
                    cancellationToken);
                return lead is null ? Results.NotFound() : Results.Ok(Map(lead));
            });

        return endpoints;
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
            lead.CreatedAtUtc);
}
