using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Meridian.E2E;

public class PortalSmokeTests : IClassFixture<PortalFactory>
{
    private readonly PortalFactory _factory;

    public PortalSmokeTests(PortalFactory factory) => _factory = factory;

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/signup")]
    [InlineData("/forgot-password")]
    public async Task Public_pages_return_200(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/app/acme")]
    [InlineData("/app/acme/opportunities")]
    [InlineData("/app/acme/pipeline")]
    [InlineData("/app/acme/enrichment")]
    [InlineData("/app/acme/activity")]
    [InlineData("/app/acme/settings")]
    [InlineData("/app/acme/settings/outbound")]
    [InlineData("/app/acme/sources")]
    [InlineData("/app/acme/members")]
    public async Task Authorized_pages_redirect_anonymous_users_to_login(string path)
    {
        var client = NoRedirectClient();
        var response = await client.GetAsync(path);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Contain("/login");
    }

    [Fact]
    public async Task Signup_with_valid_form_redirects_to_verify_email_sent()
    {
        var client = NoRedirectClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Test User",
            ["Email"] = $"smoke-{Guid.NewGuid():N}@meridian.test",
            ["Password"] = "supersecret-password-123",
            ["TenantName"] = "Smoke Workspace",
            ["TenantSlug"] = $"smoke-{Guid.NewGuid().ToString("N").Substring(0, 8)}"
        });

        var response = await client.PostAsync("/auth/signup", form);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().StartWith("/verify-email-sent");
    }

    [Fact]
    public async Task Signup_with_short_password_redirects_back_with_error()
    {
        var client = NoRedirectClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Test User",
            ["Email"] = $"weakpw-{Guid.NewGuid():N}@meridian.test",
            ["Password"] = "short",
            ["TenantName"] = "Weak Workspace",
            ["TenantSlug"] = $"weak-{Guid.NewGuid().ToString("N").Substring(0, 8)}"
        });

        var response = await client.PostAsync("/auth/signup", form);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().StartWith("/signup?error=");
    }

    [Fact]
    public async Task Logout_redirects_to_login_for_anonymous_caller()
    {
        var client = NoRedirectClient();
        var response = await client.PostAsync("/auth/logout", new StringContent(""));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().Be("/login");
    }

}
