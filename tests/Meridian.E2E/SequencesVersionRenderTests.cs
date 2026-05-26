using FluentAssertions;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.E2E;

// Sequences.razor rendered <td>v@t.Version</td>. Razor's "email-like text"
// heuristic treats `v@t` as a literal (e.g. user@host), so the whole cell
// rendered as the raw string "v@t.Version" instead of v1/v2/etc. Fix is
// the explicit-expression form @(t.Version).
public class SequencesVersionRenderTests : IClassFixture<AdminPortalFactory>
{
    private readonly AdminPortalFactory _factory;

    public SequencesVersionRenderTests(AdminPortalFactory factory) => _factory = factory;

    [Fact]
    public async Task SequencesPage_TemplatesTable_VersionColumn_ShowsInterpolatedNumber()
    {
        var tenant = Tenant.Create("Workspace template-version", "template-version");
        var template = OutreachTemplate.Create(
            tenant.Id, "Initial outreach probe", "RE: {{opportunity.title}}", "Body");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
            db.Tenants.Add(tenant);
            db.OutreachTemplates.Add(template);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-TenantId", tenant.Id.ToString());
        client.DefaultRequestHeaders.Add("X-Test-TenantSlug", tenant.Slug);

        var response = await client.GetAsync($"/app/{tenant.Slug}/settings/sequences");
        var html = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(
            $"the sequences page should load; status {(int)response.StatusCode}");
        html.Should().NotContain("v@t.Version",
            "the literal Razor expression must never appear in the rendered DOM");
        html.Should().Contain("<td>v1</td>",
            "the Version cell must render as v1 for a freshly created template");
    }
}
