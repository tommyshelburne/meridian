using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Infrastructure.Crm.HubSpot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class HubSpotOAuthBrokerTests
{
    private record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private static (HubSpotOAuthBroker broker, List<CapturedRequest> log) CreateBroker(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        HubSpotOAuthOptions? overrideOptions = null)
    {
        var log = new List<CapturedRequest>();
        var http = new HttpClient(new FakeHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            log.Add(new CapturedRequest(
                req.Method, req.RequestUri!,
                req.Headers.Authorization is null
                    ? null
                    : $"{req.Headers.Authorization.Scheme} {req.Headers.Authorization.Parameter}",
                body));
            return handler(req);
        }));
        var opts = Options.Create(overrideOptions ?? new HubSpotOAuthOptions
        {
            AuthorizeUrl = "https://app.hubspot.test/oauth/authorize",
            TokenUrl = "https://api.hubspot.test/oauth/v1/token",
            ClientId = "client-abc",
            ClientSecret = "secret-xyz",
            Scope = "crm.objects.deals.write"
        });
        return (new HubSpotOAuthBroker(http, opts, NullLogger<HubSpotOAuthBroker>.Instance), log);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json")
        };

    [Fact]
    public void Provider_is_HubSpot()
    {
        var (broker, _) = CreateBroker(_ => throw new InvalidOperationException());
        broker.Provider.Should().Be(CrmProvider.HubSpot);
    }

    [Fact]
    public void BuildAuthorizeUrl_includes_client_id_state_and_scope()
    {
        var (broker, _) = CreateBroker(_ => throw new InvalidOperationException());
        var url = broker.BuildAuthorizeUrl("state-9", "https://meridian.test/cb");

        url.Should().StartWith("https://app.hubspot.test/oauth/authorize?");
        url.Should().Contain("client_id=client-abc");
        url.Should().Contain("state=state-9");
        url.Should().Contain("redirect_uri=https%3A%2F%2Fmeridian.test%2Fcb");
        url.Should().Contain("scope=crm.objects.deals.write");
    }

    [Fact]
    public void BuildAuthorizeUrl_throws_when_client_id_unconfigured()
    {
        var (broker, _) = CreateBroker(_ => throw new InvalidOperationException(),
            new HubSpotOAuthOptions { TokenUrl = "x", AuthorizeUrl = "x" });
        FluentActions.Invoking(() => broker.BuildAuthorizeUrl("s", "r"))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_sends_credentials_in_body_and_returns_tokens()
    {
        var (broker, log) = CreateBroker(_ => Json(new
        {
            access_token = "ACC",
            refresh_token = "REF",
            expires_in = 1800
        }));

        var result = await broker.ExchangeAuthorizationCodeAsync(
            "code-1", "https://meridian.test/cb", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("ACC");
        result.Value.RefreshToken.Should().Be("REF");
        result.Value.ApiBaseUrl.Should().BeNull("HubSpot uses a fixed API host; ApiBaseUrl stays null");
        result.Value.ExpiresAt.Should().NotBeNull()
            .And.Subject.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(25));

        log.Should().ContainSingle();
        log[0].Authorization.Should().BeNull("HubSpot puts credentials in the body, not Basic auth");
        log[0].Body.Should().Contain("grant_type=authorization_code");
        log[0].Body.Should().Contain("client_id=client-abc");
        log[0].Body.Should().Contain("client_secret=secret-xyz");
        log[0].Body.Should().Contain("redirect_uri=https%3A%2F%2Fmeridian.test%2Fcb");
        log[0].Body.Should().Contain("code=code-1");
    }

    [Fact]
    public async Task Refresh_sends_refresh_grant_with_credentials()
    {
        var (broker, log) = CreateBroker(_ => Json(new
        {
            access_token = "ACC2",
            refresh_token = "REF2",
            expires_in = 1800
        }));

        var result = await broker.RefreshAsync("old-rt", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        log[0].Body.Should().Contain("grant_type=refresh_token");
        log[0].Body.Should().Contain("refresh_token=old-rt");
        log[0].Body.Should().Contain("client_id=client-abc");
        log[0].Body.Should().Contain("client_secret=secret-xyz");
    }

    [Fact]
    public async Task Returns_failure_on_4xx()
    {
        var (broker, _) = CreateBroker(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"status\":\"BAD_AUTH_CODE\"}")
        });

        var result = await broker.ExchangeAuthorizationCodeAsync("c", "r", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("400");
        result.Error.Should().Contain("BAD_AUTH_CODE");
    }

    [Fact]
    public async Task Returns_failure_when_credentials_unconfigured()
    {
        var (broker, _) = CreateBroker(_ => throw new InvalidOperationException("HTTP should not be called"),
            new HubSpotOAuthOptions { TokenUrl = "x", ClientId = "", ClientSecret = "" });

        var result = await broker.RefreshAsync("rt", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("client credentials");
    }
}
