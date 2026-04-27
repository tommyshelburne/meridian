using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Infrastructure.Crm.Salesforce;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class SalesforceOAuthBrokerTests
{
    private record CapturedRequest(HttpMethod Method, Uri Uri, string Body);

    private static (SalesforceOAuthBroker broker, List<CapturedRequest> log) Create(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        SalesforceOAuthOptions? overrideOptions = null)
    {
        var log = new List<CapturedRequest>();
        var http = new HttpClient(new FakeHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            log.Add(new CapturedRequest(req.Method, req.RequestUri!, body));
            return handler(req);
        }));
        var opts = Options.Create(overrideOptions ?? new SalesforceOAuthOptions
        {
            AuthorizeUrl = "https://login.salesforce.test/services/oauth2/authorize",
            TokenUrl = "https://login.salesforce.test/services/oauth2/token",
            ClientId = "client-abc",
            ClientSecret = "secret-xyz",
            Scope = "api refresh_token",
            ApiVersion = "v59.0"
        });
        return (new SalesforceOAuthBroker(http, opts, NullLogger<SalesforceOAuthBroker>.Instance), log);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json")
        };

    [Fact]
    public void Provider_is_Salesforce()
    {
        var (broker, _) = Create(_ => throw new InvalidOperationException());
        broker.Provider.Should().Be(CrmProvider.Salesforce);
    }

    [Fact]
    public void BuildAuthorizeUrl_includes_response_type_client_id_state_scope()
    {
        var (broker, _) = Create(_ => throw new InvalidOperationException());
        var url = broker.BuildAuthorizeUrl("state-7", "https://meridian.test/cb");

        url.Should().StartWith("https://login.salesforce.test/services/oauth2/authorize?");
        url.Should().Contain("response_type=code");
        url.Should().Contain("client_id=client-abc");
        url.Should().Contain("state=state-7");
        url.Should().Contain("scope=api%20refresh_token");
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_returns_tokens_with_versioned_api_base()
    {
        var (broker, log) = Create(_ => Json(new
        {
            access_token = "ACC",
            refresh_token = "REF",
            instance_url = "https://acme.my.salesforce.test",
            token_type = "Bearer",
            issued_at = "1700000000"
        }));

        var result = await broker.ExchangeAuthorizationCodeAsync(
            "auth-code", "https://meridian.test/cb", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("ACC");
        result.Value.RefreshToken.Should().Be("REF");
        result.Value.ExpiresAt.Should().BeNull("Salesforce omits expires_in by default");
        result.Value.ApiBaseUrl.Should().Be("https://acme.my.salesforce.test/services/data/v59.0/");

        log[0].Body.Should().Contain("grant_type=authorization_code");
        log[0].Body.Should().Contain("code=auth-code");
        log[0].Body.Should().Contain("client_id=client-abc");
        log[0].Body.Should().Contain("client_secret=secret-xyz");
    }

    [Fact]
    public async Task Refresh_sends_refresh_grant_and_returns_new_instance_url()
    {
        var (broker, log) = Create(_ => Json(new
        {
            access_token = "ACC2",
            instance_url = "https://acme.my.salesforce.test/",
            token_type = "Bearer"
        }));

        var result = await broker.RefreshAsync("old-rt", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("ACC2");
        result.Value.ApiBaseUrl.Should().Be("https://acme.my.salesforce.test/services/data/v59.0/");
        log[0].Body.Should().Contain("grant_type=refresh_token");
        log[0].Body.Should().Contain("refresh_token=old-rt");
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_propagates_4xx_body()
    {
        var (broker, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"expired authorization code\"}")
        });

        var result = await broker.ExchangeAuthorizationCodeAsync("c", "r", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("400");
        result.Error.Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Returns_failure_when_credentials_unconfigured()
    {
        var (broker, _) = Create(_ => throw new InvalidOperationException("HTTP should not be called"),
            new SalesforceOAuthOptions { TokenUrl = "x", ClientId = "", ClientSecret = "", ApiVersion = "v59.0" });

        var result = await broker.RefreshAsync("rt", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("client credentials");
    }

    [Fact]
    public void BuildApiBaseUrl_appends_services_data_version_path()
    {
        SalesforceOAuthBroker.BuildApiBaseUrl("https://acme.my.salesforce.test", "v59.0")
            .Should().Be("https://acme.my.salesforce.test/services/data/v59.0/");
        SalesforceOAuthBroker.BuildApiBaseUrl("https://acme.my.salesforce.test/", "v59.0")
            .Should().Be("https://acme.my.salesforce.test/services/data/v59.0/");
        SalesforceOAuthBroker.BuildApiBaseUrl(null, "v59.0").Should().BeNull();
        SalesforceOAuthBroker.BuildApiBaseUrl("  ", "v59.0").Should().BeNull();
    }
}
