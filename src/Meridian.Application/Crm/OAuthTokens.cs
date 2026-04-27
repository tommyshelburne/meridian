namespace Meridian.Application.Crm;

// Provider-agnostic OAuth token bundle. Brokers return one of these from
// authorize-code exchange or refresh-token refresh; the connection service
// normalizes it onto the CrmConnection aggregate.
public record OAuthTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? ApiBaseUrl);
