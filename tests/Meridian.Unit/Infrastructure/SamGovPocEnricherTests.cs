using System.Net;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Ingestion.SamGov;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class SamGovPocEnricherTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static SamGovPocEnricher CreateEnricher(
        string apiKey, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHandler(handler));
        var options = Options.Create(new SamGovOptions
        {
            ApiKey = apiKey,
            BaseUrl = "https://api.sam.gov/opportunities/v2/search"
        });
        return new SamGovPocEnricher(httpClient, options, NullLogger<SamGovPocEnricher>.Instance);
    }

    private static Opportunity CreateOpportunity()
        => Opportunity.Create(TenantId, $"NOTICE-{Guid.NewGuid()}", OpportunitySource.SamGov,
            "Contact Center RFP", "Description",
            Agency.Create("GSA", AgencyType.FederalCivilian),
            DateTimeOffset.UtcNow,
            naicsCode: "561422");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Returns_empty_without_any_http_call_when_api_key_is_missing(string apiKey)
    {
        var enricher = CreateEnricher(apiKey,
            _ => throw new InvalidOperationException("Enricher must not call SAM.gov without an API key"));

        var result = await enricher.EnrichAsync(CreateOpportunity(), TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Calls_sam_gov_when_an_api_key_is_configured()
    {
        var called = false;
        var enricher = CreateEnricher("test-key", _ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var result = await enricher.EnrichAsync(CreateOpportunity(), TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        called.Should().BeTrue("a configured API key must still issue SAM.gov requests");
    }
}
