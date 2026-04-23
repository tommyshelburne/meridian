using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Outreach.Graph;

public class GraphTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly MeridianGraphOptions _options;
    private readonly ILogger<GraphTokenProvider> _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public GraphTokenProvider(HttpClient httpClient, IOptions<MeridianGraphOptions> options,
        ILogger<GraphTokenProvider> logger)
        : this(httpClient, options.Value, logger, () => DateTimeOffset.UtcNow)
    {
    }

    public GraphTokenProvider(HttpClient httpClient, MeridianGraphOptions options,
        ILogger<GraphTokenProvider> logger, Func<DateTimeOffset> clock)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _clock = clock;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var safetyWindow = TimeSpan.FromSeconds(_options.TokenExpirySafetySeconds);
        if (_cachedToken is not null && _clock() + safetyWindow < _expiresAt)
            return _cachedToken;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && _clock() + safetyWindow < _expiresAt)
                return _cachedToken;

            var url = $"{_options.LoginBaseUrl.TrimEnd('/')}/{_options.TenantId}/oauth2/v2.0/token";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret,
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials"
                })
            };

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty token response from Entra.");

            if (string.IsNullOrEmpty(token.AccessToken))
                throw new InvalidOperationException("Entra returned an empty access_token.");

            _cachedToken = token.AccessToken;
            _expiresAt = _clock().AddSeconds(token.ExpiresIn);
            _logger.LogInformation("Acquired Graph token; expires at {ExpiresAt}", _expiresAt);
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
}
