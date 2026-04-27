using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Infrastructure.Crm.Pipedrive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class PipedriveOAuthBrokerTests
{
    private static PipedriveOAuthBroker CreateBroker(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        PipedriveOAuthOptions? overrideOptions = null)
    {
        var http = new HttpClient(new FakeHandler(handler));
        var opts = Options.Create(overrideOptions ?? new PipedriveOAuthOptions
        {
            AuthorizeUrl = "https://oauth.pipedrive.test/authorize",
            TokenUrl = "https://oauth.pipedrive.test/token",
            ClientId = "client-abc",
            ClientSecret = "secret-xyz",
            Scope = "deals:full"
        });
        return new PipedriveOAuthBroker(http, opts, NullLogger<PipedriveOAuthBroker>.Instance);
    }

    [Fact]
    public void Provider_is_Pipedrive()
    {
        var broker = CreateBroker(_ => throw new InvalidOperationException());
        broker.Provider.Should().Be(CrmProvider.Pipedrive);
    }

    [Fact]
    public void BuildAuthorizeUrl_includes_client_id_state_redirect_and_scope()
    {
        var broker = CreateBroker(_ => throw new InvalidOperationException());
        var url = broker.BuildAuthorizeUrl("state-123", "https://meridian.test/callback");

        url.Should().StartWith("https://oauth.pipedrive.test/authorize?");
        url.Should().Contain("client_id=client-abc");
        url.Should().Contain("state=state-123");
        url.Should().Contain("redirect_uri=https%3A%2F%2Fmeridian.test%2Fcallback");
        url.Should().Contain("scope=deals%3Afull");
    }

    [Fact]
    public void BuildAuthorizeUrl_throws_when_client_id_unconfigured()
    {
        var broker = CreateBroker(_ => throw new InvalidOperationException(),
            new PipedriveOAuthOptions { TokenUrl = "x", AuthorizeUrl = "x" });
        FluentActions.Invoking(() => broker.BuildAuthorizeUrl("s", "r"))
            .Should().Throw<InvalidOperationException>();
    }

    private record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private static CapturedRequest Capture(HttpRequestMessage req)
    {
        var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
        return new CapturedRequest(
            req.Method, req.RequestUri!,
            req.Headers.Authorization is null
                ? null
                : $"{req.Headers.Authorization.Scheme} {req.Headers.Authorization.Parameter}",
            body);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_returns_tokens_with_normalized_api_base()
    {
        CapturedRequest? captured = null;
        var broker = CreateBroker(req =>
        {
            captured = Capture(req);
            return JsonResponse(new
            {
                access_token = "ACC",
                refresh_token = "REF",
                token_type = "Bearer",
                expires_in = 3600,
                api_domain = "https://acme.pipedrive.test"
            });
        });

        var result = await broker.ExchangeAuthorizationCodeAsync(
            "auth-code", "https://meridian.test/callback", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("ACC");
        result.Value.RefreshToken.Should().Be("REF");
        result.Value.ExpiresAt.Should().NotBeNull()
            .And.Subject.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(50));
        result.Value.ApiBaseUrl.Should().Be("https://acme.pipedrive.test/v1/");

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.Uri.ToString().Should().Be("https://oauth.pipedrive.test/token");
        captured.Authorization.Should().StartWith("Basic ");
        var basicParam = captured.Authorization!["Basic ".Length..];
        Encoding.UTF8.GetString(Convert.FromBase64String(basicParam)).Should().Be("client-abc:secret-xyz");
        captured.Body.Should().Contain("grant_type=authorization_code");
        captured.Body.Should().Contain("code=auth-code");
        captured.Body.Should().Contain("redirect_uri=https%3A%2F%2Fmeridian.test%2Fcallback");
    }

    [Fact]
    public async Task Refresh_sends_refresh_grant_and_unwraps_envelope()
    {
        CapturedRequest? captured = null;
        var broker = CreateBroker(req =>
        {
            captured = Capture(req);
            return JsonResponse(new
            {
                access_token = "ACC2",
                refresh_token = "REF2",
                expires_in = 1800,
                api_domain = "https://acme.pipedrive.test/"
            });
        });

        var result = await broker.RefreshAsync("old-refresh-token", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("ACC2");
        result.Value.RefreshToken.Should().Be("REF2");

        captured!.Body.Should().Contain("grant_type=refresh_token");
        captured.Body.Should().Contain("refresh_token=old-refresh-token");
    }

    [Fact]
    public async Task Returns_failure_on_4xx_with_truncated_body()
    {
        var broker = CreateBroker(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\"}")
        });

        var result = await broker.ExchangeAuthorizationCodeAsync("c", "r", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("400");
        result.Error.Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Returns_failure_when_credentials_unconfigured()
    {
        var broker = CreateBroker(_ => throw new InvalidOperationException("HTTP should not be called"),
            new PipedriveOAuthOptions { TokenUrl = "x", ClientId = "", ClientSecret = "" });

        var result = await broker.RefreshAsync("rt", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("client credentials");
    }

    [Fact]
    public void NormalizeApiBase_appends_v1_when_missing()
    {
        PipedriveOAuthBroker.NormalizeApiBase("https://acme.pipedrive.com").Should().Be("https://acme.pipedrive.com/v1/");
        PipedriveOAuthBroker.NormalizeApiBase("https://acme.pipedrive.com/").Should().Be("https://acme.pipedrive.com/v1/");
        PipedriveOAuthBroker.NormalizeApiBase("https://acme.pipedrive.com/v1").Should().Be("https://acme.pipedrive.com/v1/");
        PipedriveOAuthBroker.NormalizeApiBase("https://acme.pipedrive.com/v1/").Should().Be("https://acme.pipedrive.com/v1/");
        PipedriveOAuthBroker.NormalizeApiBase(null).Should().BeNull();
        PipedriveOAuthBroker.NormalizeApiBase("  ").Should().BeNull();
    }

    private static HttpResponseMessage JsonResponse(object body)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
