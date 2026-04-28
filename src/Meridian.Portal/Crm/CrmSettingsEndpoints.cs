using Meridian.Application.Crm;
using Meridian.Portal.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Crm;

public static class CrmSettingsEndpoints
{
    public static IEndpointRouteBuilder MapCrmSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/app/{slug}/settings/crm")
            .RequireAuthorization()
            .DisableAntiforgery();

        group.MapPost("/disconnect", async (
            string slug,
            HttpContext http,
            CrmConnectionService service,
            CancellationToken ct) =>
        {
            if (!TryGetTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            var result = await service.DeactivateAsync(tenantId, ct);
            return result.IsSuccess
                ? Redirect(slug, saved: "1")
                : Redirect(slug, error: result.Error);
        });

        group.MapPost("/pipeline", async (
            string slug,
            HttpContext http,
            [FromForm] string? pipelineId,
            CrmConnectionService service,
            CancellationToken ct) =>
        {
            if (!TryGetTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            var trimmed = string.IsNullOrWhiteSpace(pipelineId) ? null : pipelineId.Trim();
            var result = await service.SetDefaultPipelineIdAsync(tenantId, trimmed, ct);
            return result.IsSuccess
                ? Redirect(slug, saved: "1")
                : Redirect(slug, error: result.Error);
        });

        return app;
    }

    private static bool TryGetTenantId(HttpContext http, out Guid tenantId)
        => Guid.TryParse(http.User.FindFirst(ClaimsBuilder.TenantIdClaim)?.Value, out tenantId);

    private static IResult Redirect(string slug, string? saved = null, string? error = null)
    {
        var query = saved is not null
            ? $"?saved={Uri.EscapeDataString(saved)}"
            : $"?error={Uri.EscapeDataString(error ?? "Unknown error.")}";
        return Results.Redirect($"/app/{slug}/settings/crm{query}");
    }
}
