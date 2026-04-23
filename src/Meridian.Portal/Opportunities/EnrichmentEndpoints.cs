using Meridian.Application.Opportunities;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Opportunities;

public static class EnrichmentEndpoints
{
    public static IEndpointRouteBuilder MapEnrichmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/app/{slug}/enrichment/add", async (
            string slug,
            HttpContext http,
            [FromForm] AddContactForm form,
            ManualEnrichmentService enrichment,
            CancellationToken ct) =>
        {
            var claim = http.User.FindFirst(Auth.ClaimsBuilder.TenantIdClaim)?.Value;
            if (!Guid.TryParse(claim, out var tenantId))
                return Results.Redirect($"/app/{slug}/enrichment?error={Uri.EscapeDataString("Session expired.")}");

            if (!Guid.TryParse(form.OpportunityId, out var opportunityId))
                return Results.Redirect($"/app/{slug}/enrichment?error={Uri.EscapeDataString("Invalid opportunity.")}");

            var result = await enrichment.AddContactAsync(
                tenantId, opportunityId,
                form.FullName ?? string.Empty,
                form.Email ?? string.Empty,
                form.Title,
                ct);

            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/enrichment?saved=1")
                : Results.Redirect($"/app/{slug}/enrichment?error={Uri.EscapeDataString(result.Error!)}");
        }).RequireAuthorization().DisableAntiforgery();

        return app;
    }
}

public class AddContactForm
{
    public string? OpportunityId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Title { get; set; }
}
