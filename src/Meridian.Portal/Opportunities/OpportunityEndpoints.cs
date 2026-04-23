using Meridian.Application.Opportunities;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Opportunities;

public static class OpportunityEndpoints
{
    public static IEndpointRouteBuilder MapOpportunityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/app/{slug}/opportunities/decide", async (
            string slug,
            HttpContext http,
            [FromForm] DecideOpportunityForm form,
            OpportunityQueueService queue,
            CancellationToken ct) =>
        {
            var claim = http.User.FindFirst(Auth.ClaimsBuilder.TenantIdClaim)?.Value;
            if (!Guid.TryParse(claim, out var tenantId))
                return Results.Redirect($"/app/{slug}/opportunities?error={Uri.EscapeDataString("Session expired.")}");

            if (!Guid.TryParse(form.OpportunityId, out var opportunityId))
                return Results.Redirect($"/app/{slug}/opportunities?error={Uri.EscapeDataString("Invalid opportunity.")}");

            if (!Enum.TryParse<QueueDecision>(form.Decision, out var decision))
                return Results.Redirect($"/app/{slug}/opportunities?error={Uri.EscapeDataString("Unknown decision.")}");

            var result = await queue.ApplyDecisionAsync(tenantId, opportunityId, decision, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/opportunities?saved=1")
                : Results.Redirect($"/app/{slug}/opportunities?error={Uri.EscapeDataString(result.Error!)}");
        }).RequireAuthorization().DisableAntiforgery();

        return app;
    }
}

public class DecideOpportunityForm
{
    public string? OpportunityId { get; set; }
    public string? Decision { get; set; }
}
