using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion.MyBidMatch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure;

public class MyBidMatchAdapterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private const string SubscriptionIndexHtml = """
        <html><body>
          <a href="/go?doc=UUID-001">March 4 2026</a>
          <a href="/go?doc=UUID-002">March 3 2026</a>
          <a href="/about">About</a>
        </body></html>
        """;

    private const string DocGroupHtml = """
        <html><body>
          <a href="/article?doc=UUID-001&seq=1">Bid 1</a>
          <a href="/article?doc=UUID-001&seq=2">Bid 2</a>
          <a href="/go?sub=ABC">Back</a>
        </body></html>
        """;

    private const string ArticleHtmlWithPreamble = """
        <html><body>
        Utah Division of Technology Services
        Some boilerplate text about APEX procurement.
        Contact APEX at 801-538-8775 for more information.
        <h4>UTAH, DIVISION OF TECHNOLOGY SERVICES</h4>
        B -- Contact Center Modernization Initiative SOL UTD-2026-001 DUE 04/15/2026
        The agency seeks IVR modernization for citizen services compliance and audit trail.
        </body></html>
        """;

    private const string ArticleHtmlWithThirdPartyDisclaimer = """
        <html><body>
        Preamble 801-538-8775
        <h4>California - California Purchasing Group</h4>
        This opportunity was identified by a third party research firm as a
        potential business opportunity.
        Q - Call Center Support Services Due Date: 03/31/2026
        The agency seeks call center support for unemployment insurance.
        More content after disclaimer.
        </body></html>
        """;

    // ── Parser tests (pure, no HTTP) ───────────────────────────────────────────

    [Fact]
    public void ParseDocGroupIds_ExtractsUuids_FromIndexHtml()
    {
        var ids = MyBidMatchParser.ParseDocGroupIds(SubscriptionIndexHtml);
        ids.Should().BeEquivalentTo(["UUID-001", "UUID-002"]);
    }

    [Fact]
    public void ParseArticleIds_BuildsDocColonSeqFormat()
    {
        var ids = MyBidMatchParser.ParseArticleIds(DocGroupHtml, "UUID-001");
        ids.Should().BeEquivalentTo(["UUID-001:1", "UUID-001:2"]);
    }

    [Fact]
    public void ParseArticle_StripsApexPreamble_AtPhoneMarker()
    {
        var article = MyBidMatchParser.ParseArticle(ArticleHtmlWithPreamble, "UUID-001:1");
        article.Body.Should().NotContain("801-538-8775");
        article.Body.Should().NotContain("boilerplate");
    }

    [Fact]
    public void ParseArticle_PreservesContent_AfterPreamble()
    {
        var article = MyBidMatchParser.ParseArticle(ArticleHtmlWithPreamble, "UUID-001:1");
        article.Body.Should().Contain("IVR modernization");
        article.Body.Should().Contain("compliance");
    }

    [Fact]
    public void ParseArticle_StripsThirdPartyDisclaimer()
    {
        var article = MyBidMatchParser.ParseArticle(ArticleHtmlWithThirdPartyDisclaimer, "UUID-001:2");
        article.Body.Should().NotContain("potential business opportunity");
        article.Body.Should().NotContain("third party research firm");
    }

    [Fact]
    public void ParseArticle_ExtractsTitleAndAgency()
    {
        var article = MyBidMatchParser.ParseArticle(ArticleHtmlWithPreamble, "UUID-001:1");
        article.Title.Should().Be("Contact Center Modernization Initiative");
        article.Agency.Should().Be("UTAH, DIVISION OF TECHNOLOGY SERVICES");
    }

    [Fact]
    public void ParseArticle_ThirdPartyBid_ExtractsTitleAndAgency()
    {
        var article = MyBidMatchParser.ParseArticle(ArticleHtmlWithThirdPartyDisclaimer, "UUID-001:2");
        article.Title.Should().Be("Call Center Support Services");
        article.Agency.Should().Be("California - California Purchasing Group");
    }

    // ── Parameters tests ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReturnsNull_WhenJsonEmpty()
    {
        MyBidMatchParameters.Parse(null).Should().BeNull();
        MyBidMatchParameters.Parse("").Should().BeNull();
        MyBidMatchParameters.Parse("{}").Should().BeNull();
    }

    [Fact]
    public void Parse_ReturnsNull_WhenSubscriptionIdMissing()
    {
        MyBidMatchParameters.Parse("""{"baseUrl":"https://example.com"}""").Should().BeNull();
    }

    [Fact]
    public void Parse_AppliesDefaults_ForOptionalFields()
    {
        var p = MyBidMatchParameters.Parse("""{"subscriptionId":"abc"}""");
        p.Should().NotBeNull();
        p!.SubscriptionId.Should().Be("abc");
        p.BaseUrl.Should().Be("https://mybidmatch.outreachsystems.com");
        p.AgencyState.Should().Be("UT");
    }

    [Fact]
    public void Parse_TrimsTrailingSlash_FromBaseUrl()
    {
        var p = MyBidMatchParameters.Parse("""{"subscriptionId":"abc","baseUrl":"https://x.com/"}""");
        p!.BaseUrl.Should().Be("https://x.com");
    }

    // ── Adapter tests (HTTP faked) ─────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_FailsResult_WhenSubscriptionIdMissing()
    {
        var adapter = BuildAdapter(_ => HtmlResponse(SubscriptionIndexHtml));
        var source = SourceDefinition.Create(TenantId, SourceAdapterType.StatePortal, "Utah", "{}");

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("subscriptionId");
    }

    [Fact]
    public async Task FetchAsync_FailsResult_WhenIndexFetchFails()
    {
        var adapter = BuildAdapter(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var source = BuildSource("ABC123");

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("index fetch failed");
    }

    [Fact]
    public async Task FetchAsync_WalksIndexThroughArticles_AndMapsToIngested()
    {
        var adapter = BuildAdapter(req =>
        {
            var path = req.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("seq=")) return HtmlResponse(ArticleHtmlWithPreamble);
            if (path.Contains("doc=UUID-001")) return HtmlResponse(DocGroupHtml);
            if (path.Contains("doc=UUID-002")) return HtmlResponse("<html><body></body></html>");
            return HtmlResponse(SubscriptionIndexHtml);
        });

        var result = await adapter.FetchAsync(BuildSource("ABC123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value!.Should().AllSatisfy(o =>
        {
            o.AgencyType.Should().Be(AgencyType.StateLocal);
            o.AgencyState.Should().Be("UT");
            o.Title.Should().Be("Contact Center Modernization Initiative");
            o.AgencyName.Should().Be("UTAH, DIVISION OF TECHNOLOGY SERVICES");
        });
        result.Value.Select(o => o.ExternalId).Should().BeEquivalentTo(["UUID-001:1", "UUID-001:2"]);
    }

    [Fact]
    public async Task FetchAsync_SkipsDocGroup_WhenDocGroupFetchFails()
    {
        var adapter = BuildAdapter(req =>
        {
            var path = req.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("seq=")) return HtmlResponse(ArticleHtmlWithPreamble);
            if (path.Contains("doc=UUID-001"))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (path.Contains("doc=UUID-002")) return HtmlResponse(DocGroupHtml);
            return HtmlResponse(SubscriptionIndexHtml);
        });

        var result = await adapter.FetchAsync(BuildSource("ABC123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchAsync_SkipsArticle_WhenTitleEmpty()
    {
        var adapter = BuildAdapter(req =>
        {
            var path = req.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("seq="))
                return HtmlResponse("<html><body><h4>Agency</h4></body></html>");
            if (path.Contains("doc=")) return HtmlResponse(DocGroupHtml);
            return HtmlResponse(SubscriptionIndexHtml);
        });

        var result = await adapter.FetchAsync(BuildSource("ABC123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void AdapterType_IsStatePortal()
    {
        var adapter = BuildAdapter(_ => HtmlResponse(""));
        adapter.AdapterType.Should().Be(SourceAdapterType.StatePortal);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static MyBidMatchAdapter BuildAdapter(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new FakeHandler(handler));
        return new MyBidMatchAdapter(http, NullLogger<MyBidMatchAdapter>.Instance);
    }

    private static SourceDefinition BuildSource(string subscriptionId)
    {
        var json = JsonSerializer.Serialize(new
        {
            subscriptionId,
            baseUrl = "https://mybidmatch.outreachsystems.com",
            agencyState = "UT"
        });
        return SourceDefinition.Create(TenantId, SourceAdapterType.StatePortal, "Utah – MyBidMatch", json);
    }

    private static HttpResponseMessage HtmlResponse(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };
}
