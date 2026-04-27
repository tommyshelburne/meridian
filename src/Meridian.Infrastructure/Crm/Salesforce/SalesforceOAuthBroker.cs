using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Crm.Salesforce;

// Salesforce OAuth 2 broker. Mirrors HubSpot's body-based credential
// posting. The token response carries the per-tenant `instance_url`; the
// broker normalizes it to `{instance_url}/services/data/{version}/` and
// hands that to the connection as ApiBaseUrl. Refresh tokens require the
// connected app's scope to include `refresh_token` / `offline_access`.
public class SalesforceOAuthBroker : ICrmOAuthBroker
{
    private readonly HttpClient _httpClient;
    private readonly SalesforceOAuthOptions _options;
    private readonly ILogger<SalesforceOAuthBroker> _logger;

    public CrmProvider Provider => CrmProvider.Salesforce;

    public SalesforceOAuthBroker(
        HttpClient httpClient,
        IOptions<SalesforceOAuthOptions> options,
        ILogger<SalesforceOAuthBroker> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string BuildAuthorizeUrl(string state, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("Salesforce:OAuth:ClientId is not configured.");

        var query = new List<string>
        {
            "response_type=code",
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
            return ServiceResult<OAuthTokens>.Fail("Salesforce OAuth client credentials are not configured.");

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
                    $"Salesforce token endpoint {(int)response.StatusCode}: {Truncate(body, 500)}");
            }

            var payload = await response.Content.ReadFromJsonAsync<SalesforceTokenResponse>(ct);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
                return ServiceResult<OAuthTokens>.Fail("Salesforce token endpoint returned no access_token.");

            // Salesforce omits expires_in by default. When it's absent we leave
            // ExpiresAt null — CrmConnectionService skips refresh until it sees
            // an expiry, so 401 from the API is the trigger to reconnect.
            var expiresAt = payload.ExpiresIn > 0
                ? DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn)
                : (DateTimeOffset?)null;

            return ServiceResult<OAuthTokens>.Ok(new OAuthTokens(
                payload.AccessToken,
                payload.RefreshToken,
                expiresAt,
                BuildApiBaseUrl(payload.InstanceUrl, _options.ApiVersion)));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Salesforce token request failed");
            return ServiceResult<OAuthTokens>.Fail($"Salesforce token request failed: {ex.Message}");
        }
    }

    public static string? BuildApiBaseUrl(string? instanceUrl, string apiVersion)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl)) return null;
        var trimmed = instanceUrl.TrimEnd('/');
        return $"{trimmed}/services/data/{apiVersion}/";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private class SalesforceTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("instance_url")] public string? InstanceUrl { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("issued_at")] public string? IssuedAt { get; set; }
    }
}
