using Meridian.Application.Auth;
using Meridian.Domain.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Auth;

public static class SsoSettingsEndpoints
{
    public static IEndpointRouteBuilder MapSsoSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/app/{slug}/settings/sso")
            .RequireAuthorization()
            .DisableAntiforgery();

        group.MapPost("/create", async (
            string slug,
            HttpContext http,
            [FromForm] SsoCreateForm form,
            OidcConfigService service,
            CancellationToken ct) =>
        {
            if (!TryGetTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            if (!Enum.TryParse<OidcProvider>(form.Provider, out var provider))
                return Redirect(slug, error: "Unknown provider.");

            var request = new CreateOidcConfigRequest(
                ProviderKey: form.ProviderKey ?? "",
                Provider: provider,
                DisplayName: form.DisplayName ?? "",
                Authority: form.Authority ?? "",
                ClientId: form.ClientId ?? "",
                ClientSecret: form.ClientSecret ?? "",
                Scopes: Blank(form.Scopes),
                EmailClaim: Blank(form.EmailClaim),
                NameClaim: Blank(form.NameClaim));

            var result = await service.CreateAsync(tenantId, request, ct);
            return result.IsSuccess
                ? Redirect(slug, saved: "1")
                : Redirect(slug, error: result.Error);
        });

        group.MapPost("/{configId:guid}/rotate-secret", async (
            string slug, Guid configId,
            [FromForm] string newSecret,
            OidcConfigService service,
            CancellationToken ct) =>
        {
            var result = await service.RotateSecretAsync(configId, newSecret ?? "", ct);
            return result.IsSuccess ? Redirect(slug, saved: "1") : Redirect(slug, error: result.Error);
        });

        group.MapPost("/{configId:guid}/enable", async (
            string slug, Guid configId,
            OidcConfigService service, CancellationToken ct) =>
        {
            await service.SetEnabledAsync(configId, true, ct);
            return Redirect(slug, saved: "1");
        });

        group.MapPost("/{configId:guid}/disable", async (
            string slug, Guid configId,
            OidcConfigService service, CancellationToken ct) =>
        {
            await service.SetEnabledAsync(configId, false, ct);
            return Redirect(slug, saved: "1");
        });

        group.MapPost("/{configId:guid}/delete", async (
            string slug, Guid configId,
            OidcConfigService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(configId, ct);
            return Redirect(slug, saved: "1");
        });

        return app;
    }

    private static bool TryGetTenantId(HttpContext http, out Guid tenantId)
        => Guid.TryParse(http.User.FindFirst(ClaimsBuilder.TenantIdClaim)?.Value, out tenantId);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Redirect(string slug, string? saved = null, string? error = null)
    {
        var query = saved is not null
            ? $"?saved={Uri.EscapeDataString(saved)}"
            : $"?error={Uri.EscapeDataString(error ?? "Unknown error.")}";
        return Results.Redirect($"/app/{slug}/settings/sso{query}");
    }
}

public class SsoCreateForm
{
    public string? Provider { get; set; }
    public string? ProviderKey { get; set; }
    public string? DisplayName { get; set; }
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scopes { get; set; }
    public string? EmailClaim { get; set; }
    public string? NameClaim { get; set; }
}
