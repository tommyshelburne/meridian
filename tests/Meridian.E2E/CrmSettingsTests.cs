using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Meridian.Portal.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.E2E;

/// <summary>
/// Boots the portal in-process with an always-on test authentication handler so
/// we can test authenticated page renders. Each test uses an isolated TenantId so
/// seeded rows never bleed between test cases sharing the same in-memory database.
/// </summary>
public class CrmSettingsFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"e2e-crm-{Guid.NewGuid():N}";

    private readonly IServiceProvider _efInternalServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Meridian", "Host=test;Database=test;Username=test");
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MeridianDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<MeridianDbContext>(opts =>
                opts.UseInMemoryDatabase(_dbName)
                    .UseInternalServiceProvider(_efInternalServices));

            services.AddHostedService<EnsureCreatedHostedService>();

            // Add the test auth handler as a named scheme, then promote it to
            // the default authenticate scheme. Requests that carry the
            // X-Test-TenantId header are authenticated as Owner; requests
            // without it return NoResult so [Authorize] still challenges → /login.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                // Keep cookie as the challenge scheme so unauthorized requests
                // still redirect to /login exactly as in production.
                opts.DefaultChallengeScheme = "Cookies";
            });
        });
    }

    /// <summary>Seeds a Tenant + optionally a CRM connection for use in tests.</summary>
    public async Task<(Guid TenantId, string Slug)> SeedTenantAsync(string slug)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();

        var existing = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
        if (existing is not null) return (existing.Id, existing.Slug);

        var tenant = Tenant.Create($"Workspace {slug}", slug);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return (tenant.Id, tenant.Slug);
    }

    public async Task SeedCrmConnectionAsync(Guid tenantId)
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<CrmConnectionService>();
        var tokens = new OAuthTokens("test-access-token", null, null, null);
        var result = await svc.ConnectFromTokensAsync(
            tenantId, CrmProvider.Pipedrive, tokens, ct: CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"seed CRM connection failed: {result.Error}");
    }

    /// <summary>
    /// Returns a client that sends X-Test-TenantId / X-Test-TenantSlug headers so
    /// <see cref="TestAuthHandler"/> authenticates every request as the given tenant.
    /// </summary>
    public HttpClient AuthenticatedClient(Guid tenantId, string slug)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-TenantSlug", slug);
        return client;
    }
}

/// <summary>
/// Test authentication handler. Authenticates any request that carries the
/// X-Test-TenantId header; returns NoResult otherwise (anonymous path).
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-TenantId", out var tenantIdHeader) ||
            !Guid.TryParse(tenantIdHeader, out var tenantId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var slug = Request.Headers.TryGetValue("X-Test-TenantSlug", out var slugHeader)
            ? slugHeader.ToString()
            : "test";

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimsBuilder.TenantIdClaim, tenantId.ToString()),
            new Claim(ClaimsBuilder.TenantSlugClaim, slug),
            new Claim(ClaimTypes.Role, "Owner"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class CrmSettingsTests : IClassFixture<CrmSettingsFactory>
{
    private readonly CrmSettingsFactory _factory;

    public CrmSettingsTests(CrmSettingsFactory factory) => _factory = factory;

    // ── (a) No existing connection ────────────────────────────────────────────

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
        var (tenantId, slug) = await _factory.SeedTenantAsync("crm-no-conn");
        var client = _factory.AuthenticatedClient(tenantId, slug);

        var response = await client.GetAsync($"/app/{slug}/settings/crm");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        // All three provider slugs must appear as OAuth connect hrefs.
        html.Should().Contain($"/app/{slug}/crm/connect/pipedrive",
            "Pipedrive connect link should be rendered when no connection exists");
        html.Should().Contain($"/app/{slug}/crm/connect/hubspot",
            "HubSpot connect link should be rendered when no connection exists");
        html.Should().Contain($"/app/{slug}/crm/connect/salesforce",
            "Salesforce connect link should be rendered when no connection exists");
    }

    // ── (b) Existing connection ───────────────────────────────────────────────

    [Fact]
    public async Task Authenticated_get_with_connection_shows_summary()
    {
        var (tenantId, slug) = await _factory.SeedTenantAsync("crm-with-conn");
        await _factory.SeedCrmConnectionAsync(tenantId);

        var client = _factory.AuthenticatedClient(tenantId, slug);
        var response = await client.GetAsync($"/app/{slug}/settings/crm");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        // Provider name and active status pill must appear in the summary table.
        html.Should().Contain("Pipedrive",
            "Provider name should appear in the connection summary");
        html.Should().Contain("Active",
            "Active status pill should appear when connection IsActive");

        // The disconnect button posts to the /disconnect sub-route.
        html.Should().Contain($"/app/{slug}/settings/crm/disconnect",
            "Disconnect form action should be present for an active connection");
    }
}
