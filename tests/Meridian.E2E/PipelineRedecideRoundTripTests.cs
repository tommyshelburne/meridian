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
/// Regression guard for the re-decide round-trip through
/// /app/{slug}/opportunities/decide. Both the Opportunities queue and the
/// OpportunityDetail action bar POST to this single endpoint. Re-deciding a
/// Pursuing opportunity to Watching must move the card across Pipeline columns
/// and leave no phantom in the original column.
/// </summary>
public class PipelineRedecideRoundTripTests : IClassFixture<AdminPortalFactory>
{
    private readonly AdminPortalFactory _factory;

    public PipelineRedecideRoundTripTests(AdminPortalFactory factory) => _factory = factory;

    [Fact]
    public async Task Redeciding_a_pursuing_opportunity_to_watching_moves_the_card_between_columns()
    {
        var tenant = Tenant.Create("Workspace redecide-board", "redecide-board");
        var opp = NewPursuingOpportunity(tenant.Id, "Re-decide Round Trip Probe");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
            db.Tenants.Add(tenant);
            db.Opportunities.Add(opp);
            await db.SaveChangesAsync();
        }

        var client = CreateClient(tenant);

        var initial = await client.GetStringAsync($"/app/{tenant.Slug}/pipeline");
        initial.Should().Contain("Re-decide Round Trip Probe",
            "the test card should start out rendered on the Pipeline board");

        var post = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["OpportunityId"] = opp.Id.ToString(),
            ["Decision"] = "Watch"
        });
        var decide = await client.PostAsync($"/app/{tenant.Slug}/opportunities/decide", post);
        decide.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect,
            "a successful decision redirects to the queue");

        var after = await client.GetStringAsync($"/app/{tenant.Slug}/pipeline");
        after.Should().Contain("Re-decide Round Trip Probe",
            "the re-decided card still belongs on the board, just under a different column");

        var pursuingIdx = after.IndexOf("class=\"column pursuing\"", StringComparison.Ordinal);
        var watchingIdx = after.IndexOf("class=\"column watching\"", StringComparison.Ordinal);
        var titleIdx = after.IndexOf("Re-decide Round Trip Probe", StringComparison.Ordinal);

        pursuingIdx.Should().BeGreaterThan(-1, "Pursuing column should render");
        watchingIdx.Should().BeGreaterThan(pursuingIdx, "Watching column renders after Pursuing");
        titleIdx.Should().BeGreaterThan(watchingIdx,
            "the re-decided card should live inside the Watching column, not the Pursuing column");
    }

    [Fact]
    public async Task Decide_endpoint_redirects_back_to_queue_with_saved_marker_after_a_successful_decision()
    {
        var tenant = Tenant.Create("Workspace redecide-marker", "redecide-marker");
        var opp = NewScoredOpportunity(tenant.Id, "Redirect Marker Probe");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
            db.Tenants.Add(tenant);
            db.Opportunities.Add(opp);
            await db.SaveChangesAsync();
        }

        var client = CreateClient(tenant);

        var post = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["OpportunityId"] = opp.Id.ToString(),
            ["Decision"] = "Pursue"
        });
        var response = await client.PostAsync($"/app/{tenant.Slug}/opportunities/decide", post);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString
            .Should().Be($"/app/{tenant.Slug}/opportunities?saved=1");
    }

    private HttpClient CreateClient(Tenant tenant)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-TenantId", tenant.Id.ToString());
        client.DefaultRequestHeaders.Add("X-Test-TenantSlug", tenant.Slug);
        return client;
    }

    private static Opportunity NewScoredOpportunity(Guid tenantId, string title)
    {
        var opp = Opportunity.Create(
            tenantId, $"EXT-REDECIDE-{Guid.NewGuid():N}", OpportunitySource.SamGov,
            title, "Body",
            Agency.Create("Re-decide Test Agency", AgencyType.StateLocal, "UT"),
            DateTimeOffset.UtcNow);
        opp.ApplyScore(BidScore.Create(11, ScoreVerdict.Pursue));
        return opp;
    }

    private static Opportunity NewPursuingOpportunity(Guid tenantId, string title)
    {
        var opp = NewScoredOpportunity(tenantId, title);
        opp.Pursue();
        return opp;
    }
}
