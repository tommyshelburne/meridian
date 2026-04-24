using System.Security.Claims;
using Meridian.Application.Auth;
using Meridian.Domain.Tenants;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Meridian.Portal.Auth.Oidc;


// Runs on IOptionsMonitor<OpenIdConnectOptions>.Get(name) — once per scheme name per
// app lifetime, since the framework caches options by name. Must be registered BEFORE
// AddOpenIdConnect so it executes before the framework's OpenIdConnectPostConfigureOptions
// (which validates Authority/ClientId are present and sets up the backchannel).
//
// Sync-over-async on ResolveByProviderKeyAsync is unavoidable: IPostConfigureOptions
// has no async variant and ASP.NET Core's own OIDC PostConfigureOptions does the same
// for backchannel discovery.
public class OidcOptionsConfigurer : IPostConfigureOptions<OpenIdConnectOptions>
{
    private readonly IServiceScopeFactory _scopes;

    public OidcOptionsConfigurer(IServiceScopeFactory scopes) => _scopes = scopes;

    public void PostConfigure(string? name, OpenIdConnectOptions options)
    {
        if (name is null) return;
        if (!OidcSchemeNames.TryParse(name, out var tenantId, out var providerKey)) return;

        using var scope = _scopes.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<OidcConfigService>();
        var config = configService
            .ResolveByProviderKeyAsync(tenantId, providerKey, default)
            .GetAwaiter().GetResult();
        if (config is null) return;

        options.Authority = config.Authority;
        options.ClientId = config.ClientId;
        options.ClientSecret = config.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.SignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.CallbackPath = $"/auth/oidc/{providerKey}/callback";
        options.SignedOutCallbackPath = $"/auth/oidc/{providerKey}/signout-callback";
        options.RemoteSignOutPath = $"/auth/oidc/{providerKey}/signout";

        options.Scope.Clear();
        foreach (var scopeName in config.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            options.Scope.Add(scopeName);

        options.TokenValidationParameters.NameClaimType = config.NameClaim;

        // Convert the OIDC ticket into Meridian's cookie-shaped principal. After
        // OnTokenValidated runs, the framework signs in to the cookie scheme using
        // ctx.Principal, so replacing it here is what persists our claims (UserId,
        // TenantId, TenantSlug, Role) for the rest of the session.
        var emailClaim = config.EmailClaim;
        var nameClaim = config.NameClaim;
        var capturedTenantId = tenantId;
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async ctx =>
            {
                var email = ctx.Principal?.FindFirst(emailClaim)?.Value
                            ?? ctx.Principal?.FindFirst(ClaimTypes.Email)?.Value
                            ?? ctx.Principal?.FindFirst("preferred_username")?.Value;

                if (string.IsNullOrWhiteSpace(email))
                {
                    ctx.Fail("OIDC token missing email claim.");
                    return;
                }

                var displayName = ctx.Principal?.FindFirst(nameClaim)?.Value
                                  ?? ctx.Principal?.FindFirst(ClaimTypes.Name)?.Value
                                  ?? email;

                var sp = ctx.HttpContext.RequestServices;
                var authService = sp.GetRequiredService<AuthService>();
                var tenantContext = sp.GetRequiredService<ITenantContext>();

                var result = await authService.SignInWithOidcAsync(
                    capturedTenantId, email, displayName, ctx.HttpContext.RequestAborted);
                if (!result.IsSuccess || result.Memberships.Count == 0)
                {
                    ctx.Fail($"SSO sign-in rejected: {result.Outcome}.");
                    return;
                }

                var membership = result.Memberships[0];
                tenantContext.SetTenant(membership.TenantId);
                ctx.Principal = ClaimsBuilder.Build(result, membership);
                ctx.Properties!.RedirectUri = $"/app/{membership.TenantSlug}";
            }
        };
    }
}
