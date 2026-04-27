using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Domain.Common;

namespace Meridian.Application.Ports;

public interface ICrmOAuthBroker
{
    CrmProvider Provider { get; }

    // Returns the absolute URL the user should be redirected to for consent.
    // The implementation is responsible for embedding the supplied state value.
    string BuildAuthorizeUrl(string state, string redirectUri);

    Task<ServiceResult<OAuthTokens>> ExchangeAuthorizationCodeAsync(
        string code, string redirectUri, CancellationToken ct);

    Task<ServiceResult<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken ct);
}

public interface ICrmOAuthBrokerFactory
{
    ICrmOAuthBroker Resolve(CrmProvider provider);
    bool TryResolve(CrmProvider provider, out ICrmOAuthBroker broker);
}
