using System.Net;
using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Auth;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenantContext = Meridian.Infrastructure.Persistence.TenantContext;

namespace Meridian.E2E;

public class OidcCacheInvalidationTests : IClassFixture<AdminPortalFactory>
{
    private readonly AdminPortalFactory _factory;

    public OidcCacheInvalidationTests(AdminPortalFactory factory) => _factory = factory;

    // -------------------------------------------------------------------------
    // Disable: scheme removed from cache on /disable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Disable_clears_scheme_cache_so_challenge_returns_sso_not_configured()
    {
        var tenant = await SeedTenantAsync("inval-disable");
        var configId = await SeedEnabledConfigAsync(tenant.Id, "inval-entra");

        // Warm the scheme cache by resolving the scheme directly.
        var schemeName = OidcSchemeNames.Format(tenant.Id, "inval-entra");
        var schemeProvider = _factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var cached = await schemeProvider.GetSchemeAsync(schemeName);
        cached.Should().NotBeNull("scheme must be resolvable before disabling");

        // Disable via the admin endpoint.
        var adminClient = AdminClient(tenant.Id, "inval-disable");
        var response = await adminClient.PostAsync(
            $"/app/inval-disable/settings/sso/{configId}/disable",
            new FormUrlEncodedContent([]));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);

        // Scheme cache should now be empty — GetSchemeAsync re-checks DB and finds disabled.
        var afterDisable = await schemeProvider.GetSchemeAsync(schemeName);
        afterDisable.Should().BeNull("disabled scheme must not remain in cache after /disable");
    }

    [Fact]
    public async Task Disable_causes_challenge_endpoint_to_return_sso_not_configured()
    {
        var tenant = await SeedTenantAsync("inval-disable2");
        var configId = await SeedEnabledConfigAsync(tenant.Id, "entra2");

        // Warm the scheme cache.
        var schemeName = OidcSchemeNames.Format(tenant.Id, "entra2");
        var schemeProvider = _factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        (await schemeProvider.GetSchemeAsync(schemeName)).Should().NotBeNull();

        // Disable via endpoint.
        var adminClient = AdminClient(tenant.Id, "inval-disable2");
        await adminClient.PostAsync(
            $"/app/inval-disable2/settings/sso/{configId}/disable",
            new FormUrlEncodedContent([]));

        // Challenge endpoint should now report sso-not-configured.
        var noRedirectClient = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        var challenge = await noRedirectClient.GetAsync(
            "/auth/oidc/entra2/challenge?tenant=inval-disable2");
        challenge.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        challenge.Headers.Location!.OriginalString.Should().Contain("error=sso-not-configured");
    }

    // -------------------------------------------------------------------------
    // Rotate secret: options cache cleared so next Get returns new secret
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RotateSecret_clears_options_cache_so_next_get_returns_new_secret()
    {
        var tenant = await SeedTenantAsync("inval-rotate");
        var configId = await SeedEnabledConfigAsync(tenant.Id, "entra-rotate", "original-secret");

        // Warm the options cache.
        var schemeName = OidcSchemeNames.Format(tenant.Id, "entra-rotate");
        var optionsMonitor = _factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        var before = optionsMonitor.Get(schemeName);
        before.ClientSecret.Should().Be("original-secret");

        // Rotate via endpoint.
        var adminClient = AdminClient(tenant.Id, "inval-rotate");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["newSecret"] = "rotated-secret"
        });
        var response = await adminClient.PostAsync(
            $"/app/inval-rotate/settings/sso/{configId}/rotate-secret", form);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);

        // Options cache must be gone — next Get triggers PostConfigure with the new secret.
        var after = optionsMonitor.Get(schemeName);
        after.ClientSecret.Should().Be("rotated-secret",
            "options cache must be invalidated so the rotated secret is reflected immediately");
    }

    // -------------------------------------------------------------------------
    // Delete: both caches cleared before row is removed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_clears_scheme_and_options_caches()
    {
        var tenant = await SeedTenantAsync("inval-delete");
        var configId = await SeedEnabledConfigAsync(tenant.Id, "entra-delete");

        var schemeName = OidcSchemeNames.Format(tenant.Id, "entra-delete");
        var schemeProvider = _factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var optionsMonitor = _factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

        // Warm both caches.
        (await schemeProvider.GetSchemeAsync(schemeName)).Should().NotBeNull();
        _ = optionsMonitor.Get(schemeName);

        // Delete via endpoint.
        var adminClient = AdminClient(tenant.Id, "inval-delete");
        var response = await adminClient.PostAsync(
            $"/app/inval-delete/settings/sso/{configId}/delete",
            new FormUrlEncodedContent([]));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);

        // Scheme cache should be empty — config is gone from DB so GetSchemeAsync returns null.
        var scheme = await schemeProvider.GetSchemeAsync(schemeName);
        scheme.Should().BeNull("deleted scheme must not remain in cache");

        // Options cache should be empty — next Get should return unconfigured (null Authority).
        var options = optionsMonitor.Get(schemeName);
        options.Authority.Should().BeNullOrEmpty(
            "options cache must be invalidated so deleted config is not served");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private HttpClient AdminClient(Guid tenantId, string tenantSlug)
    {
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-TenantSlug", tenantSlug);
        return client;
    }

    private async Task<Tenant> SeedTenantAsync(string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var existing = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
        if (existing is not null) return existing;

        var tenant = Tenant.Create($"Workspace {slug}", slug);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<Guid> SeedEnabledConfigAsync(
        Guid tenantId, string providerKey, string clientSecret = "test-secret")
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<OidcConfigService>();
        var result = await svc.CreateAsync(tenantId, new CreateOidcConfigRequest(
            ProviderKey: providerKey,
            Provider: OidcProvider.EntraId,
            DisplayName: $"Test {providerKey}",
            Authority: "https://login.microsoftonline.com/test-tenant/v2.0",
            ClientId: "test-client-id",
            ClientSecret: clientSecret), CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"seed setup failed: {result.Error}");
        return result.Value;
    }
}
