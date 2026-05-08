using Meridian.Application.Auth;
using Meridian.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
            IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
            IAuthenticationSchemeProvider schemeProvider,
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
            if (result.IsSuccess)
            {
                InvalidateSchemeCaches(optionsCache, schemeProvider, tenantId, form.ProviderKey ?? "");
                return Redirect(slug, saved: "1");
            }
            return Redirect(slug, error: result.Error);
        });

        group.MapPost("/{configId:guid}/rotate-secret", async (
            string slug, Guid configId,
            [FromForm] string newSecret,
            OidcConfigService service,
            IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
            IAuthenticationSchemeProvider schemeProvider,
            CancellationToken ct) =>
        {
            var result = await service.RotateSecretAsync(configId, newSecret ?? "", ct);
            if (result.IsSuccess)
            {
                var key = await service.GetSchemeKeyAsync(configId, ct);
                if (key.HasValue)
                    InvalidateSchemeCaches(optionsCache, schemeProvider, key.Value.TenantId, key.Value.ProviderKey);
                return Redirect(slug, saved: "1");
            }
            return Redirect(slug, error: result.Error);
        });

        group.MapPost("/{configId:guid}/enable", async (
            string slug, Guid configId,
            OidcConfigService service,
            IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
            IAuthenticationSchemeProvider schemeProvider,
            CancellationToken ct) =>
        {
            await service.SetEnabledAsync(configId, true, ct);
            var key = await service.GetSchemeKeyAsync(configId, ct);
            if (key.HasValue)
                InvalidateSchemeCaches(optionsCache, schemeProvider, key.Value.TenantId, key.Value.ProviderKey);
            return Redirect(slug, saved: "1");
        });

        group.MapPost("/{configId:guid}/disable", async (
            string slug, Guid configId,
            OidcConfigService service,
            IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
            IAuthenticationSchemeProvider schemeProvider,
            CancellationToken ct) =>
        {
            await service.SetEnabledAsync(configId, false, ct);
            var key = await service.GetSchemeKeyAsync(configId, ct);
            if (key.HasValue)
                InvalidateSchemeCaches(optionsCache, schemeProvider, key.Value.TenantId, key.Value.ProviderKey);
            return Redirect(slug, saved: "1");
        });

        group.MapPost("/{configId:guid}/delete", async (
            string slug, Guid configId,
            OidcConfigService service,
            IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
            IAuthenticationSchemeProvider schemeProvider,
            CancellationToken ct) =>
        {
            // Fetch key before deleting so we still have providerKey after the record is gone.
            var key = await service.GetSchemeKeyAsync(configId, ct);
            if (key.HasValue)
                InvalidateSchemeCaches(optionsCache, schemeProvider, key.Value.TenantId, key.Value.ProviderKey);
            await service.DeleteAsync(configId, ct);
            return Redirect(slug, saved: "1");
        });

        return app;
    }

    private static void InvalidateSchemeCaches(
        IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
        IAuthenticationSchemeProvider schemeProvider,
        Guid tenantId, string providerKey)
    {
        var schemeName = OidcSchemeNames.Format(tenantId, providerKey);
        optionsCache.TryRemove(schemeName);
        schemeProvider.RemoveScheme(schemeName);
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
