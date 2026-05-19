using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Meridian.E2E;

// Failing-before-implementation set (Memento TDD Red, testSetId 6c5eedfd-d86c-435d-90b7-a6e492ed7304,
// linked to scenarioId ffd47efc-cea2-4783-b73e-99121ef62405). These nine tests pin the public
// marketing surface: anonymous 200 at /, /pricing, /features, /about; hero CTA + sign-in link;
// SEO meta tags; no auth-driven redirect at /; static SSR (no Blazor interactive descriptors).
public class MarketingLandingPageTests : IClassFixture<PortalFactory>
{
    private readonly PortalFactory _factory;

    public MarketingLandingPageTests(PortalFactory factory) => _factory = factory;

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    [Fact]
    public async Task AnonymousGetRoot_Returns200WithMarketingContent()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Meridian");
        body.Should().Contain("government procurement",
            because: "the marketing hero must communicate the product domain");
    }

    [Fact]
    public async Task AnonymousGetPricing_Returns200()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/pricing");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousGetFeatures_Returns200()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/features");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousGetAbout_Returns200()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/about");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Root_ContainsPrimaryCallToAction()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Get a demo",
            because: "primary CTA on the landing must be visible in rendered HTML");
    }

    [Fact]
    public async Task Root_ContainsSignInLink()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("href=\"/login\"",
            because: "the marketing header must offer anon users a path to sign in");
    }

    [Fact]
    public async Task Root_ContainsSeoMetaTags()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("<meta name=\"description\"",
            because: "SEO description must render in <head>");
        body.Should().Contain("property=\"og:title\"",
            because: "Open Graph title must render for link previews");
    }

    [Fact]
    public async Task AuthenticatedGetRoot_ServesLandingNotRedirect()
    {
        // The previous minimal-API redirect at "/" returned 302 for every request.
        // The landing must serve 200 to all callers. We send a junk auth cookie
        // (cookie-auth unprotect will fail and the principal stays anonymous, but
        // that's enough to assert the new route is not the old auth-aware redirect).
        var client = NoRedirectClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", "meridian_auth=irrelevant");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MarketingPages_RenderStaticServerSide()
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        // Blazor emits "<!--Blazor:" descriptor comments for interactive components.
        // Marketing pages must render pure static SSR for SEO and zero-latency TTFB.
        body.Should().NotContain("<!--Blazor:",
            because: "marketing pages must render static SSR, not InteractiveServer");
    }
}
