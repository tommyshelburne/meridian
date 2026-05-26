using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.E2E;

// OpportunityDetail.razor builds Score breakdown rows from a Row(...) local
// function that returns RenderFragment. The helper must be invoked as
// @Row(...). Wrapping it in @{ ... } builds the fragment and discards it,
// leaving the breakdown <ul> empty regardless of the underlying values.
// Same footgun PR #15 fixed on Pipeline.razor.
public class OpportunityDetailScoreBreakdownTests : IClassFixture<AdminPortalFactory>
{
    private readonly AdminPortalFactory _factory;

    public OpportunityDetailScoreBreakdownTests(AdminPortalFactory factory) => _factory = factory;

    [Fact]
    public async Task OpportunityDetail_ScoreBreakdown_RendersAll8Dimensions()
    {
        var tenant = Tenant.Create("Workspace score-breakdown", "score-breakdown");
        var opportunity = Opportunity.Create(
            tenant.Id, "EXT-SCORE-1", OpportunitySource.SamGov,
            "Score Breakdown Render Probe", "An opportunity with a non-trivial breakdown.",
            Agency.Create("Score Test Agency", AgencyType.StateLocal, "UT"),
            DateTimeOffset.UtcNow);

        var breakdown = BidScoreBreakdown.Create(
            laneTitle: 2, laneDescription: 1, agencyTier: 2, winThemes: 2,
            pastPerformance: 2, procurementVehicle: 0, seatCount: 2, recompete: 1);
        opportunity.ApplyScore(BidScore.Create(breakdown, ScoreVerdict.Pursue, recompeteDetected: true));

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

        var response = await client.GetAsync($"/app/{tenant.Slug}/opportunities/{opportunity.Id}");
        var html = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(
            $"the opportunity detail page should load; status {(int)response.StatusCode}");

        var expectedLabels = new[]
        {
            "Lane fit (title)", "Lane fit (description)", "Agency tier",
            "Win themes", "Past performance", "Procurement vehicle",
            "Seat count", "Recompete"
        };
        foreach (var label in expectedLabels)
        {
            html.Should().Contain(label,
                $"Score breakdown must render the '{label}' dimension row");
        }
        html.Should().Contain("class=\"breakdown\"",
            "the breakdown <ul> should be present");
    }
}
