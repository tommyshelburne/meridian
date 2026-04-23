using Meridian.Application.Opportunities;

namespace Meridian.Portal.Opportunities;

public static class DevSeedEndpoints
{
    /// <summary>
    /// Dev-only endpoint to seed a handful of demo opportunities for the current
    /// tenant. Only registered when the host environment is "Development" so it
    /// can't be hit in production.
    /// </summary>
    public static IEndpointRouteBuilder MapDevSeedEndpoints(this IEndpointRouteBuilder app, IWebHostEnvironment env)
    {
        if (!env.IsDevelopment()) return app;

        app.MapPost("/app/{slug}/opportunities/seed", async (
            string slug,
            HttpContext http,
            DevSeedService seed,
            CancellationToken ct) =>
        {
            var claim = http.User.FindFirst(Auth.ClaimsBuilder.TenantIdClaim)?.Value;
            if (!Guid.TryParse(claim, out var tenantId))
                return Results.Redirect($"/app/{slug}/opportunities?error={Uri.EscapeDataString("Session expired.")}");

            var result = await seed.SeedSampleOpportunitiesAsync(tenantId, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/opportunities?saved=1")
                : Results.Redirect($"/app/{slug}/opportunities?error={Uri.EscapeDataString(result.Error!)}");
        }).RequireAuthorization().DisableAntiforgery();

        return app;
    }
}
