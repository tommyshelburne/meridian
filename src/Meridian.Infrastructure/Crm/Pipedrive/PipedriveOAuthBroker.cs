using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Crm.Pipedrive;

// Implements the OAuth 2 authorization-code + refresh-token flows for
// Pipedrive. The token endpoint requires HTTP Basic auth on
// (client_id, client_secret), with grant parameters in the
// application/x-www-form-urlencoded body. The exchange response carries a
// per-tenant `api_domain` — that becomes the connection's ApiBaseUrl, and
// every subsequent adapter call routes against it.
public class PipedriveOAuthBroker : ICrmOAuthBroker
{
    private readonly HttpClient _httpClient;
    private readonly PipedriveOAuthOptions _options;
    private readonly ILogger<PipedriveOAuthBroker> _logger;

    public CrmProvider Provider => CrmProvider.Pipedrive;

    public PipedriveOAuthBroker(
        HttpClient httpClient,
        IOptions<PipedriveOAuthOptions> options,
        ILogger<PipedriveOAuthBroker> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string BuildAuthorizeUrl(string state, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("Pipedrive:OAuth:ClientId is not configured.");

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
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        }, ct);

    public Task<ServiceResult<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, ct);

    private async Task<ServiceResult<OAuthTokens>> PostTokenAsync(
        IDictionary<string, string> form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            return ServiceResult<OAuthTokens>.Fail("Pipedrive OAuth client credentials are not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return ServiceResult<OAuthTokens>.Fail(
                    $"Pipedrive token endpoint {(int)response.StatusCode}: {Truncate(body, 500)}");
            }

            var payload = await response.Content.ReadFromJsonAsync<PipedriveTokenResponse>(ct);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
                return ServiceResult<OAuthTokens>.Fail("Pipedrive token endpoint returned no access_token.");

            var apiBase = NormalizeApiBase(payload.ApiDomain);
            var expiresAt = payload.ExpiresIn > 0
                ? DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn)
                : (DateTimeOffset?)null;

            return ServiceResult<OAuthTokens>.Ok(new OAuthTokens(
                payload.AccessToken,
                payload.RefreshToken,
                expiresAt,
                apiBase));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Pipedrive token request failed");
            return ServiceResult<OAuthTokens>.Fail($"Pipedrive token request failed: {ex.Message}");
        }
    }

    // Pipedrive returns api_domain as a host root (e.g. https://acme.pipedrive.com).
    // Adapter builds URLs against `{base}/v1/...` so we standardize on that suffix
    // here — keeps the adapter ignorant of the OAuth-vs-personal-token distinction.
    public static string? NormalizeApiBase(string? apiDomain)
    {
        if (string.IsNullOrWhiteSpace(apiDomain)) return null;
        var trimmed = apiDomain.TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed + "/"
            : trimmed + "/v1/";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private class PipedriveTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("api_domain")] public string? ApiDomain { get; set; }
    }
}
