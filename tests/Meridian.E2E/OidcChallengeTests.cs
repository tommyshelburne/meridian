using System.Net;
using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Auth;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantContext = Meridian.Infrastructure.Persistence.TenantContext;

namespace Meridian.E2E;

public class OidcChallengeTests : IClassFixture<PortalFactory>
{
    private readonly PortalFactory _factory;

    public OidcChallengeTests(PortalFactory factory) => _factory = factory;

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    [Fact]
    public async Task Challenge_without_tenant_query_redirects_with_missing_tenant_error()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/auth/oidc/anything/challenge");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().Contain("error=missing-tenant");
    }

    [Fact]
    public async Task Challenge_with_unknown_tenant_redirects_with_unknown_tenant_error()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/auth/oidc/entra/challenge?tenant=does-not-exist");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().Contain("error=unknown-tenant");
    }

    [Fact]
    public async Task Challenge_with_unconfigured_provider_redirects_with_sso_not_configured()
    {
        await SeedTenantAsync("nosso");
        var client = NoRedirectClient();

        var response = await client.GetAsync("/auth/oidc/entra/challenge?tenant=nosso");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().Contain("error=sso-not-configured");
        response.Headers.Location.OriginalString.Should().Contain("tenant=nosso",
            "redirect should preserve the tenant slug so the login page can re-attempt SSO");
    }

    [Fact]
    public async Task Challenge_with_disabled_provider_redirects_with_sso_not_configured()
    {
        var tenant = await SeedTenantAsync("disabled-sso");
        await SeedDisabledConfigAsync(tenant.Id, "entra");
        var client = NoRedirectClient();

        var response = await client.GetAsync("/auth/oidc/entra/challenge?tenant=disabled-sso");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().Contain("error=sso-not-configured");
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

    private async Task SeedDisabledConfigAsync(Guid tenantId, string providerKey)
    {
        using var scope = _factory.Services.CreateScope();
        // Tenant context is normally set by TenantClaimMiddleware on a real request.
        // We're seeding outside the request pipeline, so set it manually so the
        // tenant query filter on OidcConfig doesn't blank out GetByIdAsync.
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<OidcConfigService>();
        var created = await svc.CreateAsync(tenantId, new CreateOidcConfigRequest(
            ProviderKey: providerKey,
            Provider: OidcProvider.EntraId,
            DisplayName: "Disabled Entra",
            Authority: "https://login.microsoftonline.com/abc/v2.0",
            ClientId: "client-id",
            ClientSecret: "client-secret"), CancellationToken.None);
        created.IsSuccess.Should().BeTrue($"seed setup failed: {created.Error}");
        var disabled = await svc.SetEnabledAsync(created.Value, false, CancellationToken.None);
        disabled.IsSuccess.Should().BeTrue($"disable failed: {disabled.Error}");
    }
}
