using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Crm;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Application.Crm;

public class CrmOAuthServiceTests
{
    private const string CallbackUri = "https://meridian.test/crm/oauth/callback/pipedrive";
    private const string ReturnUrl = "/app/acme/settings/crm";
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void BeginConnect_returns_authorize_url_when_broker_registered()
    {
        var (svc, _, brokers, _, _) = Build();
        var broker = brokers.Register(CrmProvider.Pipedrive);

        var result = svc.BeginConnect(TenantId, CrmProvider.Pipedrive, CallbackUri, ReturnUrl);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AuthorizeUrl.Should().StartWith(broker.AuthorizePrefix);
        broker.LastRedirectUri.Should().Be(CallbackUri);
    }

    [Fact]
    public void BeginConnect_fails_when_no_broker_for_provider()
    {
        var (svc, _, _, _, _) = Build();

        var result = svc.BeginConnect(TenantId, CrmProvider.HubSpot, CallbackUri, ReturnUrl);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteConnect_persists_tokens_returned_by_broker()
    {
        var (svc, repo, brokers, protector, _) = Build();
        var broker = brokers.Register(CrmProvider.Pipedrive);
        broker.NextExchangeTokens = new OAuthTokens(
            "access-1", "refresh-1", DateTimeOffset.UtcNow.AddHours(1), "https://acme.pipedrive.test/v1/");

        var begin = svc.BeginConnect(TenantId, CrmProvider.Pipedrive, CallbackUri, ReturnUrl);
        var stateToken = ExtractState(begin.Value!.AuthorizeUrl);

        var complete = await svc.CompleteConnectAsync(
            CrmProvider.Pipedrive, "auth-code", stateToken, CallbackUri, CancellationToken.None);

        complete.IsSuccess.Should().BeTrue();
        complete.Value!.TenantId.Should().Be(TenantId);
        complete.Value.ReturnUrl.Should().Be(ReturnUrl);
        broker.LastExchangeCode.Should().Be("auth-code");

        var stored = repo.All.Single();
        stored.TenantId.Should().Be(TenantId);
        stored.Provider.Should().Be(CrmProvider.Pipedrive);
        stored.EncryptedAuthToken.Should().Be("ENC:access-1");
        stored.EncryptedRefreshToken.Should().Be("ENC:refresh-1");
        stored.ApiBaseUrl.Should().Be("https://acme.pipedrive.test/v1/");
        protector.ProtectCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CompleteConnect_rejects_tampered_state()
    {
        var (svc, _, brokers, _, _) = Build();
        brokers.Register(CrmProvider.Pipedrive);

        var result = await svc.CompleteConnectAsync(
            CrmProvider.Pipedrive, "code", "garbage-not-protected", CallbackUri, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("State");
    }

    [Fact]
    public async Task CompleteConnect_rejects_provider_mismatch()
    {
        var (svc, _, brokers, _, stateProtector) = Build();
        brokers.Register(CrmProvider.Pipedrive);

        // Manually craft a state for HubSpot but call complete with Pipedrive.
        var crossState = new CrmOAuthState(
            TenantId, CrmProvider.HubSpot, ReturnUrl, DateTimeOffset.UtcNow.AddMinutes(5), "n");
        var token = stateProtector.Protect(crossState);

        var result = await svc.CompleteConnectAsync(
            CrmProvider.Pipedrive, "code", token, CallbackUri, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("mismatch");
    }

    [Fact]
    public async Task CompleteConnect_rejects_expired_state()
    {
        var (svc, _, brokers, _, stateProtector) = Build();
        brokers.Register(CrmProvider.Pipedrive);

        var expired = new CrmOAuthState(
            TenantId, CrmProvider.Pipedrive, ReturnUrl, DateTimeOffset.UtcNow.AddMinutes(-1), "n");
        var token = stateProtector.Protect(expired);

        var result = await svc.CompleteConnectAsync(
            CrmProvider.Pipedrive, "code", token, CallbackUri, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task CompleteConnect_propagates_broker_exchange_failure()
    {
        var (svc, _, brokers, _, _) = Build();
        var broker = brokers.Register(CrmProvider.Pipedrive);
        broker.ExchangeError = "invalid_grant";

        var begin = svc.BeginConnect(TenantId, CrmProvider.Pipedrive, CallbackUri, ReturnUrl);
        var stateToken = ExtractState(begin.Value!.AuthorizeUrl);

        var result = await svc.CompleteConnectAsync(
            CrmProvider.Pipedrive, "code", stateToken, CallbackUri, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("invalid_grant");
    }

    private static string ExtractState(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');
        var pair = query.Split('&').First(p => p.StartsWith("state="));
        return Uri.UnescapeDataString(pair["state=".Length..]);
    }

    private static (CrmOAuthService svc, FakeConnectionRepo repo, FakeBrokerFactory brokers,
                    FakeProtector protector, FakeStateProtector stateProtector) Build()
    {
        var repo = new FakeConnectionRepo();
        var protector = new FakeProtector();
        var brokers = new FakeBrokerFactory();
        var stateProtector = new FakeStateProtector();
        var connections = new CrmConnectionService(
            repo, protector, brokers, NullLogger<CrmConnectionService>.Instance);
        var svc = new CrmOAuthService(
            brokers, stateProtector, connections, NullLogger<CrmOAuthService>.Instance);
        return (svc, repo, brokers, protector, stateProtector);
    }

    private class FakeConnectionRepo : ICrmConnectionRepository
    {
        public List<CrmConnection> All { get; } = new();
        public Task<CrmConnection?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.TenantId == tenantId));
        public Task<CrmConnection?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.Id == id));
        public Task<IReadOnlyList<CrmConnection>> ListRefreshableExpiringBeforeAsync(
            DateTimeOffset cutoff, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CrmConnection>>(Array.Empty<CrmConnection>());
        public Task AddAsync(CrmConnection connection, CancellationToken ct)
        {
            All.Add(connection);
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }
        public string Protect(string plaintext) { ProtectCalls++; return $"ENC:{plaintext}"; }
        public string Unprotect(string ciphertext) =>
            ciphertext.StartsWith("ENC:") ? ciphertext[4..] : throw new InvalidOperationException("not encrypted");
    }

    private class FakeStateProtector : ICrmOAuthStateProtector
    {
        // Test impl: round-trips via JSON. Production uses ASP.NET Data Protection.
        public string Protect(CrmOAuthState state) =>
            System.Text.Json.JsonSerializer.Serialize(state);

        public bool TryUnprotect(string token, out CrmOAuthState? state)
        {
            state = null;
            try { state = System.Text.Json.JsonSerializer.Deserialize<CrmOAuthState>(token); }
            catch { return false; }
            return state is not null;
        }
    }

    private class FakeBrokerFactory : ICrmOAuthBrokerFactory
    {
        public Dictionary<CrmProvider, FakeBroker> Brokers { get; } = new();

        public FakeBroker Register(CrmProvider provider)
        {
            var broker = new FakeBroker(provider);
            Brokers[provider] = broker;
            return broker;
        }

        public ICrmOAuthBroker Resolve(CrmProvider provider) =>
            Brokers.TryGetValue(provider, out var b)
                ? b
                : throw new InvalidOperationException($"No broker for {provider}");

        public bool TryResolve(CrmProvider provider, out ICrmOAuthBroker broker)
        {
            if (Brokers.TryGetValue(provider, out var b)) { broker = b; return true; }
            broker = default!;
            return false;
        }
    }

    private class FakeBroker : ICrmOAuthBroker
    {
        public string AuthorizePrefix => $"https://broker.test/{Provider.ToString().ToLowerInvariant()}/authorize";

        public CrmProvider Provider { get; }
        public string? LastRedirectUri { get; private set; }
        public string? LastExchangeCode { get; private set; }
        public OAuthTokens? NextExchangeTokens { get; set; }
        public string? ExchangeError { get; set; }

        public FakeBroker(CrmProvider provider) => Provider = provider;

        public string BuildAuthorizeUrl(string state, string redirectUri)
        {
            LastRedirectUri = redirectUri;
            return $"{AuthorizePrefix}?state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
        }

        public Task<ServiceResult<OAuthTokens>> ExchangeAuthorizationCodeAsync(
            string code, string redirectUri, CancellationToken ct)
        {
            LastExchangeCode = code;
            if (ExchangeError is not null)
                return Task.FromResult(ServiceResult<OAuthTokens>.Fail(ExchangeError));
            return Task.FromResult(ServiceResult<OAuthTokens>.Ok(
                NextExchangeTokens ?? throw new InvalidOperationException("set NextExchangeTokens")));
        }

        public Task<ServiceResult<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken ct) =>
            Task.FromResult(ServiceResult<OAuthTokens>.Fail("not used in this test"));
    }
}
