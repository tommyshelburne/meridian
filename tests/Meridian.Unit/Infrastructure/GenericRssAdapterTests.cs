using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion.Generic;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure;

public class GenericRssAdapterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static GenericRssAdapter CreateAdapter(string feedXml)
    {
        var httpClient = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(feedXml, System.Text.Encoding.UTF8, "application/xml")
        }));
        return new GenericRssAdapter(httpClient, NullLogger<GenericRssAdapter>.Instance);
    }

    private static SourceDefinition CreateSource(object parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        return SourceDefinition.Create(TenantId, SourceAdapterType.GenericRss, "Test RSS", json);
    }

    [Fact]
    public async Task Parses_rss_2_feed_into_ingested_opportunities()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" ?>
            <rss version="2.0">
              <channel>
                <title>Agency Feed</title>
                <item>
                  <title>RFP for Contact Center Services</title>
                  <description>Seeking vendors for inbound call handling.</description>
                  <guid>RFP-2026-01</guid>
                  <pubDate>Tue, 15 Apr 2026 10:00:00 GMT</pubDate>
                </item>
                <item>
                  <title>RFQ for IT Services</title>
                  <description>Desktop support services.</description>
                  <guid>RFQ-2026-02</guid>
                  <pubDate>Wed, 16 Apr 2026 10:00:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;
        var adapter = CreateAdapter(xml);
        var source = CreateSource(new
        {
            feedUrl = "https://agency.example.com/feed.rss",
            agencyName = "Utah Division of Purchasing",
            agencyState = "UT"
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].Title.Should().Be("RFP for Contact Center Services");
        result.Value[0].ExternalId.Should().Be("RFP-2026-01");
        result.Value[0].AgencyName.Should().Be("Utah Division of Purchasing");
        result.Value[0].AgencyType.Should().Be(AgencyType.StateLocal);
        result.Value[0].AgencyState.Should().Be("UT");
    }

    [Fact]
    public async Task Parses_atom_feed()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Agency Atom</title>
              <entry>
                <id>atom-001</id>
                <title>Contact Center Modernization</title>
                <summary>Modernize legacy IVR.</summary>
                <published>2026-04-15T10:00:00Z</published>
              </entry>
            </feed>
            """;
        var adapter = CreateAdapter(xml);
        var source = CreateSource(new
        {
            feedUrl = "https://agency.example.com/feed.atom",
            agencyName = "Test Agency"
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].ExternalId.Should().Be("atom-001");
        result.Value[0].Title.Should().Be("Contact Center Modernization");
    }

    [Fact]
    public async Task Applies_include_keyword_filter()
    {
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>Contact Center RFP</title><description>x</description><guid>g1</guid></item>
              <item><title>Janitorial Services</title><description>x</description><guid>g2</guid></item>
            </channel></rss>
            """;
        var adapter = CreateAdapter(xml);
        var source = CreateSource(new
        {
            feedUrl = "https://x.example.com/feed",
            agencyName = "Test",
            includeKeywords = new[] { "contact center" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value![0].Title.Should().Be("Contact Center RFP");
    }

    [Fact]
    public async Task Applies_exclude_keyword_filter()
    {
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>Contact Center RFP</title><description>x</description><guid>g1</guid></item>
              <item><title>Contact Center Demo</title><description>DEMO ONLY</description><guid>g2</guid></item>
            </channel></rss>
            """;
        var adapter = CreateAdapter(xml);
        var source = CreateSource(new
        {
            feedUrl = "https://x.example.com/feed",
            agencyName = "Test",
            excludeKeywords = new[] { "demo only" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value![0].ExternalId.Should().Be("g1");
    }

    [Fact]
    public async Task Fails_when_feed_url_missing()
    {
        var adapter = CreateAdapter("<rss/>");
        var source = SourceDefinition.Create(
            TenantId, SourceAdapterType.GenericRss, "Test RSS", "{}");

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("feedUrl");
    }

    [Fact]
    public async Task Returns_failure_on_http_error()
    {
        var httpClient = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var adapter = new GenericRssAdapter(httpClient, NullLogger<GenericRssAdapter>.Instance);
        var source = CreateSource(new
        {
            feedUrl = "https://x.example.com/404",
            agencyName = "Test"
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("RSS fetch failed");
    }

    [Fact]
    public async Task Returns_failure_on_malformed_xml()
    {
        var adapter = CreateAdapter("<not-valid-xml");
        var source = CreateSource(new
        {
            feedUrl = "https://x.example.com/bad",
            agencyName = "Test"
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("parse");
    }

    [Fact]
    public async Task Marks_defense_when_is_defense_flag_set()
    {
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>Army RFP</title><description>x</description><guid>g1</guid></item>
            </channel></rss>
            """;
        var adapter = CreateAdapter(xml);
        var source = CreateSource(new
        {
            feedUrl = "https://x.example.com/feed",
            agencyName = "US Army",
            isDefense = true
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value![0].AgencyType.Should().Be(AgencyType.FederalDefense);
    }
}
