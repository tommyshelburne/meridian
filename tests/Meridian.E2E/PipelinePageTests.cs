using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.E2E;

/// <summary>
/// E2E coverage for the Pipeline board (/app/{slug}/pipeline).
///
/// Regression guard: the page renders each triage column from a RenderColumn
/// helper that returns a RenderFragment. The helper must be invoked as an
/// @RenderColumn(...) expression — wrapping the call in an @{ ... } statement
/// block builds the fragment and discards it, leaving the board permanently
/// empty regardless of how many opportunities have been triaged.
/// </summary>
public class PipelinePageTests : IClassFixture<AdminPortalFactory>
{
    private readonly AdminPortalFactory _factory;

    public PipelinePageTests(AdminPortalFactory factory) => _factory = factory;

    [Fact]
    public async Task Pipeline_page_renders_a_card_for_a_pursuing_opportunity()
    {
        var tenant = Tenant.Create("Workspace pipeline-board", "pipeline-board");
        var opportunity = Opportunity.Create(
            tenant.Id, "EXT-PIPE-1", OpportunitySource.SamGov,
            "Pipeline Board Render Probe", "A triaged opportunity.",
            Agency.Create("Pipeline Test Agency", AgencyType.StateLocal, "UT"),
            DateTimeOffset.UtcNow);
        opportunity.ApplyScore(BidScore.Create(11, ScoreVerdict.Pursue));
        opportunity.Pursue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
            db.Tenants.Add(tenant);
            db.Opportunities.Add(opportunity);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-TenantId", tenant.Id.ToString());
        client.DefaultRequestHeaders.Add("X-Test-TenantSlug", tenant.Slug);

        var response = await client.GetAsync($"/app/{tenant.Slug}/pipeline");
        var html = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(
            $"the pipeline page should load; status {(int)response.StatusCode}, body: {html}");
        html.Should().Contain("class=\"column pursuing\"",
            "the Pursuing column must render on the board");
        html.Should().Contain("Pipeline Board Render Probe",
            "the board must render a card for the Pursuing opportunity");
    }
}
