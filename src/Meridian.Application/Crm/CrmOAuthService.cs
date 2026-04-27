using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Crm;

public record BeginConnectResult(string AuthorizeUrl);

public class CrmOAuthService
{
    // States are short-lived: long enough to consent on the provider side,
    // short enough that a leaked link is useless. 10 minutes mirrors the
    // typical OAuth provider tolerance.
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly ICrmOAuthBrokerFactory _brokers;
    private readonly ICrmOAuthStateProtector _stateProtector;
    private readonly CrmConnectionService _connections;
    private readonly ILogger<CrmOAuthService> _logger;

    public CrmOAuthService(
        ICrmOAuthBrokerFactory brokers,
        ICrmOAuthStateProtector stateProtector,
        CrmConnectionService connections,
        ILogger<CrmOAuthService> logger)
    {
        _brokers = brokers;
        _stateProtector = stateProtector;
        _connections = connections;
        _logger = logger;
    }

    public ServiceResult<BeginConnectResult> BeginConnect(
        Guid tenantId, CrmProvider provider, string redirectUri, string returnUrl)
    {
        if (!_brokers.TryResolve(provider, out var broker))
            return ServiceResult<BeginConnectResult>.Fail($"OAuth is not configured for {provider}.");

        var state = new CrmOAuthState(
            tenantId, provider, returnUrl,
            DateTimeOffset.UtcNow.Add(StateLifetime),
            Guid.NewGuid().ToString("N"));

        try
        {
            var token = _stateProtector.Protect(state);
            var url = broker.BuildAuthorizeUrl(token, redirectUri);
            return ServiceResult<BeginConnectResult>.Ok(new BeginConnectResult(url));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "OAuth begin-connect failed for {Provider}", provider);
            return ServiceResult<BeginConnectResult>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<CrmOAuthState>> CompleteConnectAsync(
        CrmProvider provider, string code, string stateToken, string redirectUri, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return ServiceResult<CrmOAuthState>.Fail("Missing authorization code.");
        if (string.IsNullOrWhiteSpace(stateToken))
            return ServiceResult<CrmOAuthState>.Fail("Missing state.");

        if (!_stateProtector.TryUnprotect(stateToken, out var state) || state is null)
            return ServiceResult<CrmOAuthState>.Fail("State validation failed.");
        if (state.Provider != provider)
            return ServiceResult<CrmOAuthState>.Fail("State / provider mismatch.");
        if (state.ExpiresAt <= DateTimeOffset.UtcNow)
            return ServiceResult<CrmOAuthState>.Fail("State expired; restart the connection.");

        if (!_brokers.TryResolve(provider, out var broker))
            return ServiceResult<CrmOAuthState>.Fail($"OAuth is not configured for {provider}.");

        var exchange = await broker.ExchangeAuthorizationCodeAsync(code, redirectUri, ct);
        if (!exchange.IsSuccess)
            return ServiceResult<CrmOAuthState>.Fail(exchange.Error!);

        var connect = await _connections.ConnectFromTokensAsync(state.TenantId, provider, exchange.Value!, ct: ct);
        if (!connect.IsSuccess)
            return ServiceResult<CrmOAuthState>.Fail(connect.Error!);

        return ServiceResult<CrmOAuthState>.Ok(state);
    }
}
