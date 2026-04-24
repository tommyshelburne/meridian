using Meridian.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Meridian.Portal.Auth.Oidc;


// ASP.NET Core's auth pipeline expects schemes registered at startup. We need a scheme
// per (tenant, provider) pair, and tenant admins create those at runtime via the SSO
// settings page — so static registration doesn't fit.
//
// Strategy: subclass AuthenticationSchemeProvider. When asked for a scheme whose name
// matches "oidc:{tenantId}:{providerKey}", look the config up in the DB on demand,
// manufacture an AuthenticationScheme bound to OpenIdConnectHandler, and cache it via
// AddScheme. The matching IPostConfigureOptions<OpenIdConnectOptions> populates the
// per-scheme OpenIdConnectOptions when the handler initializes.
//
// One known MVP limitation: cached schemes don't refresh when an admin updates a
// config — a portal restart is required for changes to take effect. That's acceptable
// for v3.0; cache invalidation lands with the admin CRUD page.
public class DynamicOidcSchemeProvider : AuthenticationSchemeProvider
{
    private readonly IServiceScopeFactory _scopes;

    public DynamicOidcSchemeProvider(
        IOptions<AuthenticationOptions> options,
        IServiceScopeFactory scopes) : base(options)
    {
        _scopes = scopes;
    }

    public override async Task<AuthenticationScheme?> GetSchemeAsync(string name)
    {
        var scheme = await base.GetSchemeAsync(name);
        if (scheme is not null) return scheme;
        if (!OidcSchemeNames.TryParse(name, out var tenantId, out var providerKey)) return null;

        using var scope = _scopes.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<OidcConfigService>();
        var config = await configService.ResolveByProviderKeyAsync(tenantId, providerKey, default);
        if (config is null || !config.IsEnabled) return null;

        var dynamicScheme = new AuthenticationScheme(name, config.DisplayName, typeof(OpenIdConnectHandler));
        AddScheme(dynamicScheme);
        return dynamicScheme;
    }
}
