using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Crm.HubSpot;

// HubSpot OAuth 2 broker. Differs from Pipedrive in two ways: credentials
// travel inside the form body (not HTTP Basic), and the response carries no
// per-tenant api_domain — HubSpot is fronted by api.hubapi.com for every
// portal. Refresh is the same endpoint with grant_type=refresh_token.
public class HubSpotOAuthBroker : ICrmOAuthBroker
{
    private readonly HttpClient _httpClient;
    private readonly HubSpotOAuthOptions _options;
    private readonly ILogger<HubSpotOAuthBroker> _logger;

    public CrmProvider Provider => CrmProvider.HubSpot;

    public HubSpotOAuthBroker(
        HttpClient httpClient,
        IOptions<HubSpotOAuthOptions> options,
        ILogger<HubSpotOAuthBroker> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string BuildAuthorizeUrl(string state, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("HubSpot:OAuth:ClientId is not configured.");

        var query = new List<string>
        {
            $"client_id={Uri.EscapeDataString(_options.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"state={Uri.EscapeDataString(state)}"
        };
        if (!string.IsNullOrWhiteSpace(_options.Scope))
            query.Add($"scope={Uri.EscapeDataString(_options.Scope)}");

        return $"{_options.AuthorizeUrl}?{string.Join('&', query)}";
    }

    public Task<ServiceResult<OAuthTokens>> ExchangeAuthorizationCodeAsync(
        string code, string redirectUri, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["code"] = code
        }, ct);

    public Task<ServiceResult<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken
        }, ct);

    private async Task<ServiceResult<OAuthTokens>> PostTokenAsync(
        IDictionary<string, string> form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            return ServiceResult<OAuthTokens>.Fail("HubSpot OAuth client credentials are not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return ServiceResult<OAuthTokens>.Fail(
                    $"HubSpot token endpoint {(int)response.StatusCode}: {Truncate(body, 500)}");
            }

            var payload = await response.Content.ReadFromJsonAsync<HubSpotTokenResponse>(ct);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
                return ServiceResult<OAuthTokens>.Fail("HubSpot token endpoint returned no access_token.");

            var expiresAt = payload.ExpiresIn > 0
                ? DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn)
                : (DateTimeOffset?)null;

            // HubSpot has a fixed API host; ApiBaseUrl stays null so the adapter
            // falls back to the configured HubSpotOptions.BaseUrl.
            return ServiceResult<OAuthTokens>.Ok(new OAuthTokens(
                payload.AccessToken,
                payload.RefreshToken,
                expiresAt,
                ApiBaseUrl: null));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HubSpot token request failed");
            return ServiceResult<OAuthTokens>.Fail($"HubSpot token request failed: {ex.Message}");
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private class HubSpotTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }
}
