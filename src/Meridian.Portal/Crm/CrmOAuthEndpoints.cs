using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Portal.Auth;

namespace Meridian.Portal.Crm;

public static class CrmOAuthEndpoints
{
    // Tenant-scoped initiator. Pulls TenantId from the auth cookie and embeds
    // it into a protected `state` so the (anonymous, registered with Pipedrive)
    // callback can route the exchange to the right tenant.
    public static IEndpointRouteBuilder MapCrmOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/app/{slug}/crm/connect/{provider}", (
            string slug, string provider,
            HttpContext http,
            CrmOAuthService service) =>
        {
            if (!Enum.TryParse<CrmProvider>(provider, ignoreCase: true, out var parsed) ||
                parsed == CrmProvider.None)
                return Results.BadRequest("Unknown CRM provider.");

            if (!Guid.TryParse(http.User.FindFirst(ClaimsBuilder.TenantIdClaim)?.Value, out var tenantId))
                return Results.Redirect($"/login?ReturnUrl=/app/{slug}/crm/connect/{provider}");

            var redirectUri = BuildCallbackUri(http, parsed);
            var returnUrl = $"/app/{slug}/settings/crm";
            var begin = service.BeginConnect(tenantId, parsed, redirectUri, returnUrl);

            return begin.IsSuccess
                ? Results.Redirect(begin.Value!.AuthorizeUrl)
                : Results.Redirect($"{returnUrl}?error={Uri.EscapeDataString(begin.Error!)}");
        }).RequireAuthorization();

        // Provider-side redirect_uri: anonymous, tenant-agnostic. State validates
        // the round-trip; on success we redirect to the state.ReturnUrl saved at
        // the start of the flow.
        app.MapGet("/crm/oauth/callback/{provider}", async (
            string provider,
            string? code, string? state, string? error,
            HttpContext http,
            CrmOAuthService service,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<CrmProvider>(provider, ignoreCase: true, out var parsed) ||
                parsed == CrmProvider.None)
                return Results.BadRequest("Unknown CRM provider.");

            if (!string.IsNullOrEmpty(error))
                return Results.Redirect($"/?crm_error={Uri.EscapeDataString(error)}");

            var redirectUri = BuildCallbackUri(http, parsed);
            var result = await service.CompleteConnectAsync(parsed, code ?? "", state ?? "", redirectUri, ct);

            if (!result.IsSuccess)
                return Results.Redirect($"/?crm_error={Uri.EscapeDataString(result.Error!)}");

            var returnUrl = SafeReturnUrl(result.Value!.ReturnUrl);
            var sep = returnUrl.Contains('?') ? '&' : '?';
            return Results.Redirect($"{returnUrl}{sep}crm_connected={parsed}");
        });

        return app;
    }

    private static string BuildCallbackUri(HttpContext http, CrmProvider provider)
    {
        var request = http.Request;
        return $"{request.Scheme}://{request.Host}/crm/oauth/callback/{provider.ToString().ToLowerInvariant()}";
    }

    // Open-redirect guard: only honor app-relative return URLs we issued.
    private static string SafeReturnUrl(string returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";
}
