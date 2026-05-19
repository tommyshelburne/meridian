using System.Net;
using FluentAssertions;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantContext = Meridian.Infrastructure.Persistence.TenantContext;

namespace Meridian.E2E;

/// <summary>
/// E2E coverage for the source wizard POST endpoints (/app/{slug}/sources/new/*).
/// Reuses <see cref="AdminPortalFactory"/>'s X-Test-TenantId auth, like
/// <see cref="OidcCacheInvalidationTests"/>.
///
/// Regression guard: each endpoint binds an HTML form POST to a [FromForm]
/// complex type. Those types must be classes with a parameterless constructor
/// and settable properties — positional records bind to nothing and the request
/// fails with an empty-body HTTP 400 before the handler runs.
/// </summary>
public class SourceWizardTests : IClassFixture<AdminPortalFactory>
{
    private readonly AdminPortalFactory _factory;

    public SourceWizardTests(AdminPortalFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_webhook_source_with_minimal_fields_redirects_to_sources()
    {
        var tenant = await SeedTenantAsync("wiz-webhook");
        var client = AdminClient(tenant.Id, tenant.Slug);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Test webhook",
            ["AgencyName"] = "Test Agency",
            ["MapExternalId"] = "id",
            ["MapTitle"] = "title",
        });

        var response = await client.PostAsync($"/app/{tenant.Slug}/sources/new/webhook", form);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Redirect, HttpStatusCode.Found },
            $"webhook wizard POST should redirect; body was: {body}");
        response.Headers.Location!.OriginalString.Should().Contain("/sources?created=");

        await AssertSourceCreatedAsync(tenant.Id, "Test webhook");
    }

    [Fact]
    public async Task Create_rss_source_with_minimal_fields_redirects_to_sources()
    {
        var tenant = await SeedTenantAsync("wiz-rss");
        var client = AdminClient(tenant.Id, tenant.Slug);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Test RSS feed",
            ["FeedUrl"] = "https://example.gov/bids.rss",
            ["AgencyName"] = "Test Agency",
        });

        var response = await client.PostAsync($"/app/{tenant.Slug}/sources/new/rss", form);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Redirect, HttpStatusCode.Found },
            $"RSS wizard POST should redirect; body was: {body}");
        response.Headers.Location!.OriginalString.Should().Contain("/sources?created=");

        await AssertSourceCreatedAsync(tenant.Id, "Test RSS feed");
    }

    [Fact]
    public async Task Create_rest_source_with_minimal_fields_redirects_to_sources()
    {
        var tenant = await SeedTenantAsync("wiz-rest");
        var client = AdminClient(tenant.Id, tenant.Slug);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Test REST feed",
            ["Url"] = "https://api.example.com/opportunities",
            ["AgencyName"] = "Test Agency",
            ["MapExternalId"] = "id",
            ["MapTitle"] = "title",
        });

        var response = await client.PostAsync($"/app/{tenant.Slug}/sources/new/rest", form);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Redirect, HttpStatusCode.Found },
            $"REST wizard POST should redirect; body was: {body}");
        response.Headers.Location!.OriginalString.Should().Contain("/sources?created=");

        await AssertSourceCreatedAsync(tenant.Id, "Test REST feed");
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

    private async Task AssertSourceCreatedAsync(Guid tenantId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        // SourceDefinition carries a global tenant query filter, so the tenant
        // context must be set before the row is visible (see OidcCacheInvalidationTests).
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var created = await db.SourceDefinitions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Name == name);
        created.Should().NotBeNull(
            $"the wizard POST should have persisted a source named '{name}'");
    }
}
