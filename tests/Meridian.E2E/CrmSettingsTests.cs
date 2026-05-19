using System.Net;
using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantContext = Meridian.Infrastructure.Persistence.TenantContext;

namespace Meridian.E2E;

/// <summary>
/// E2E coverage for the CRM settings page (/app/{slug}/settings/crm). Reuses
/// <see cref="AdminPortalFactory"/>'s X-Test-TenantId auth scheme rather than a
/// bespoke factory, mirroring <see cref="OidcCacheInvalidationTests"/>.
/// </summary>
public class CrmSettingsTests : IClassFixture<AdminPortalFactory>
{
    private readonly AdminPortalFactory _factory;

    public CrmSettingsTests(AdminPortalFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_get_redirects_to_login()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/app/acme/settings/crm");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Contain("/login");
    }

    [Fact]
    public async Task Authenticated_get_without_connection_shows_provider_connect_links()
    {
        var tenant = await SeedTenantAsync("crm-no-conn");
        var client = AdminClient(tenant.Id, tenant.Slug);

        var response = await client.GetAsync($"/app/{tenant.Slug}/settings/crm");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain($"/app/{tenant.Slug}/crm/connect/pipedrive");
        html.Should().Contain($"/app/{tenant.Slug}/crm/connect/hubspot");
        html.Should().Contain($"/app/{tenant.Slug}/crm/connect/salesforce");
    }

    [Fact]
    public async Task Authenticated_get_with_connection_shows_summary()
    {
        var tenant = await SeedTenantAsync("crm-with-conn");
        await SeedCrmConnectionAsync(tenant.Id);
        var client = AdminClient(tenant.Id, tenant.Slug);

        var response = await client.GetAsync($"/app/{tenant.Slug}/settings/crm");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("Pipedrive");
        html.Should().Contain("Active");
        html.Should().Contain($"/app/{tenant.Slug}/settings/crm/disconnect");
    }

    [Fact]
    public async Task Authenticated_get_with_crm_error_query_param_shows_error_banner()
    {
        var tenant = await SeedTenantAsync("crm-err");
        var client = AdminClient(tenant.Id, tenant.Slug);

        var response = await client.GetAsync(
            $"/app/{tenant.Slug}/settings/crm?crm_error=Pipedrive+rejected+the+authorization");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("Pipedrive rejected the authorization",
            "the ?crm_error= value should be surfaced to the operator");
        html.Should().Contain("role=\"alert\"",
            "the crm_error message should render in an accessible alert banner");
    }

    private HttpClient AdminClient(Guid tenantId, string slug)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-TenantSlug", slug);
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

    private async Task SeedCrmConnectionAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        // CrmConnection carries a tenant query filter; seeding outside the request
        // pipeline needs TenantContext set manually (see OidcChallengeTests).
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<CrmConnectionService>();
        var tokens = new OAuthTokens("test-access-token", null, null, null);
        var result = await svc.ConnectFromTokensAsync(
            tenantId, CrmProvider.Pipedrive, tokens, ct: CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"seed CRM connection failed: {result.Error}");
    }
}
