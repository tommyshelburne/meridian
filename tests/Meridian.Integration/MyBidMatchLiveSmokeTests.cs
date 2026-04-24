using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion.MyBidMatch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Integration;

public class MyBidMatchLiveSmokeTests
{
    private const string SubscriptionEnvVar = "MERIDIAN_MYBIDMATCH_SUBSCRIPTION_ID";

    [Fact(Skip = "Hits the live mybidmatch.outreachsystems.com — remove Skip to run locally; "
              + "requires MERIDIAN_MYBIDMATCH_SUBSCRIPTION_ID env var.")]
    public async Task FetchAsync_ReturnsRealOpportunities_FromLiveSubscription()
    {
        var subscriptionId = Environment.GetEnvironmentVariable(SubscriptionEnvVar);
        subscriptionId.Should().NotBeNullOrWhiteSpace(
            $"smoke test requires {SubscriptionEnvVar} env var");

        var adapter = new MyBidMatchAdapter(new HttpClient(), NullLogger<MyBidMatchAdapter>.Instance);
        var source = SourceDefinition.Create(
            Guid.NewGuid(),
            SourceAdapterType.StatePortal,
            "Utah – MyBidMatch (smoke)",
            $$"""{"subscriptionId":"{{subscriptionId}}","agencyState":"UT"}""");

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"adapter failed: {result.Error}");
        result.Value.Should().NotBeNull().And.NotBeEmpty(
            "live subscription should return at least one opportunity");

        result.Value!.Should().AllSatisfy(o =>
        {
            o.AgencyType.Should().Be(AgencyType.StateLocal);
            o.AgencyState.Should().Be("UT");
            o.ExternalId.Should().Contain(":", "external IDs should be 'docId:seq' format");
            o.Title.Should().NotBeNullOrWhiteSpace();
            o.AgencyName.Should().NotBeNullOrWhiteSpace();
        });
    }
}
