using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion.Generic;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure;

public class GenericHtmlAdapterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static GenericHtmlAdapter CreateAdapter(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHandler(handler));
        return new GenericHtmlAdapter(httpClient, NullLogger<GenericHtmlAdapter>.Instance);
    }

    private static HttpResponseMessage HtmlResponse(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html")
        };

    private static SourceDefinition CreateSource(object parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        return SourceDefinition.Create(TenantId, SourceAdapterType.GenericHtml, "Test HTML", json);
    }

    [Fact]
    public async Task Extracts_opportunities_with_xpath_selectors()
    {
        const string html = """
            <html><body>
              <ul class="opps">
                <li class="opp" data-id="UT-001">
                  <h2 class="title">Contact Center Modernization</h2>
                  <span class="naics">561422</span>
                  <span class="posted">2026-04-15</span>
                  <span class="deadline">2026-05-30</span>
                  <span class="value">$1,250,000</span>
                </li>
                <li class="opp" data-id="UT-002">
                  <h2 class="title">IT Support Renewal</h2>
                  <span class="posted">2026-04-18</span>
                </li>
              </ul>
            </body></html>
            """;
        var adapter = CreateAdapter(_ => HtmlResponse(html));
        var source = CreateSource(new
        {
            url = "https://u3p.utah.gov/opps",
            agencyName = "Utah State Procurement",
            agencyState = "UT",
            itemXPath = "//li[@class='opp']",
            fieldMap = new
            {
                externalId = ".",
                externalIdAttribute = "data-id",
                title = ".//h2[@class='title']",
                postedDate = ".//span[@class='posted']",
                responseDeadline = ".//span[@class='deadline']",
                naicsCode = ".//span[@class='naics']",
                estimatedValue = ".//span[@class='value']"
            }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        var first = result.Value![0];
        first.ExternalId.Should().Be("UT-001");
        first.Title.Should().Be("Contact Center Modernization");
        first.AgencyName.Should().Be("Utah State Procurement");
        first.AgencyState.Should().Be("UT");
        first.AgencyType.Should().Be(AgencyType.StateLocal);
        first.NaicsCode.Should().Be("561422");
        first.EstimatedValue.Should().Be(1_250_000m);
        first.ResponseDeadline.Should().NotBeNull();
    }

    [Fact]
    public async Task Returns_empty_when_item_xpath_matches_nothing()
    {
        var adapter = CreateAdapter(_ => HtmlResponse("<html><body><p>No opps</p></body></html>"));
        var source = CreateSource(new
        {
            url = "https://example.com",
            agencyName = "Test",
            itemXPath = "//div[@class='nonexistent']",
            fieldMap = new { title = ".//h2" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_items_with_missing_title()
    {
        const string html = """
            <ul>
              <li class="opp"><h2>Has title</h2></li>
              <li class="opp"><span>No title here</span></li>
            </ul>
            """;
        var adapter = CreateAdapter(_ => HtmlResponse(html));
        var source = CreateSource(new
        {
            url = "https://example.com",
            agencyName = "Test",
            itemXPath = "//li[@class='opp']",
            fieldMap = new { title = ".//h2" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value![0].Title.Should().Be("Has title");
    }

    [Fact]
    public async Task Resolves_relative_detail_url_against_base_url()
    {
        const string html = """
            <ul>
              <li class="opp"><h2><a href="/opps/12345">Contact Center RFP</a></h2></li>
            </ul>
            """;
        var adapter = CreateAdapter(_ => HtmlResponse(html));
        var source = CreateSource(new
        {
            url = "https://u3p.utah.gov/listings",
            baseUrl = "https://u3p.utah.gov",
            agencyName = "Utah",
            agencyState = "UT",
            itemXPath = "//li[@class='opp']",
            fieldMap = new
            {
                title = ".//h2/a",
                detailUrl = ".//h2/a",
                detailUrlAttribute = "href"
            }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value![0].ExternalId.Should().Be("https://u3p.utah.gov/opps/12345");
    }

    [Fact]
    public async Task Falls_back_to_stable_hash_for_external_id_when_unspecified()
    {
        const string html = """
            <ul><li class="opp"><h2>Anonymous Opp</h2></li></ul>
            """;
        var adapter = CreateAdapter(_ => HtmlResponse(html));
        var source = CreateSource(new
        {
            url = "https://example.com/listings",
            agencyName = "Test",
            itemXPath = "//li[@class='opp']",
            fieldMap = new { title = ".//h2" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value![0].ExternalId.Should().StartWith("html-");
    }

    [Fact]
    public async Task Hash_is_stable_across_runs_for_same_input()
    {
        const string html = "<ul><li class='opp'><h2>Same Opp</h2></li></ul>";
        var adapter = CreateAdapter(_ => HtmlResponse(html));
        var source = CreateSource(new
        {
            url = "https://example.com/listings",
            agencyName = "Test",
            itemXPath = "//li[@class='opp']",
            fieldMap = new { title = ".//h2" }
        });

        var first = await adapter.FetchAsync(source, CancellationToken.None);
        var second = await adapter.FetchAsync(source, CancellationToken.None);

        first.Value![0].ExternalId.Should().Be(second.Value![0].ExternalId);
    }

    [Fact]
    public async Task Http_failure_returns_failed_result()
    {
        var adapter = CreateAdapter(_ => HtmlResponse("server down", HttpStatusCode.InternalServerError));
        var source = CreateSource(new
        {
            url = "https://example.com",
            agencyName = "Test",
            itemXPath = "//li",
            fieldMap = new { title = ".//h2" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().StartWith("HTML fetch failed");
    }

    [Fact]
    public async Task Missing_required_parameters_returns_validation_failure()
    {
        var adapter = CreateAdapter(_ => HtmlResponse("<html/>"));
        var source = CreateSource(new { url = "https://x" });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("itemXPath");
    }

    [Fact]
    public async Task Defense_flag_classifies_agency_type_as_federal_defense()
    {
        const string html = "<ul><li class='opp'><h2>DLA RFP</h2></li></ul>";
        var adapter = CreateAdapter(_ => HtmlResponse(html));
        var source = CreateSource(new
        {
            url = "https://example.mil",
            agencyName = "Defense Logistics Agency",
            isDefense = true,
            itemXPath = "//li[@class='opp']",
            fieldMap = new { title = ".//h2" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value![0].AgencyType.Should().Be(AgencyType.FederalDefense);
        result.Value[0].AgencyState.Should().BeNull();
    }

    [Fact]
    public async Task Whitespace_inside_inner_text_collapses_to_single_spaces()
    {
        const string html = """
            <ul>
              <li class="opp">
                <h2>
                  Contact   Center
                  Modernization
                </h2>
              </li>
            </ul>
            """;
        var adapter = CreateAdapter(_ => HtmlResponse(html));
        var source = CreateSource(new
        {
            url = "https://example.com",
            agencyName = "Test",
            agencyState = "UT",
            itemXPath = "//li[@class='opp']",
            fieldMap = new { title = ".//h2" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value![0].Title.Should().Be("Contact Center Modernization");
    }
}
