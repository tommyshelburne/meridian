using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Crm;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Application.Crm;

public class CrmConnectionServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Connect_persists_new_connection_with_encrypted_credentials()
    {
        var (svc, repo, protector, _) = Build();

        var result = await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "raw-token");

        result.IsSuccess.Should().BeTrue();
        repo.All.Should().ContainSingle();
        repo.All[0].EncryptedAuthToken.Should().Be("ENC:raw-token");
        protector.ProtectCalls.Should().Be(1);
    }

    [Fact]
    public async Task Connect_with_None_provider_fails()
    {
        var (svc, _, _, _) = Build();
        var result = await svc.ConnectAsync(TenantId, CrmProvider.None, "token");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_with_blank_token_fails()
    {
        var (svc, _, _, _) = Build();
        var result = await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "   ");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_when_existing_same_provider_rotates_credentials()
    {
        var (svc, repo, _, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "old-token");

        var result = await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "new-token");

        result.IsSuccess.Should().BeTrue();
        repo.All.Should().ContainSingle();
        repo.All[0].EncryptedAuthToken.Should().Be("ENC:new-token");
        repo.All[0].Provider.Should().Be(CrmProvider.Pipedrive);
    }

    [Fact]
    public async Task Connect_when_existing_different_provider_changes_provider_and_clears_pipeline()
    {
        var (svc, repo, _, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "old-token", defaultPipelineId: "p-1");

        var result = await svc.ConnectAsync(TenantId, CrmProvider.HubSpot, "new-token");

        result.IsSuccess.Should().BeTrue();
        repo.All[0].Provider.Should().Be(CrmProvider.HubSpot);
        repo.All[0].DefaultPipelineId.Should().BeNull();
    }

    [Fact]
    public async Task Connect_reactivates_a_previously_deactivated_connection()
    {
        var (svc, repo, _, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "token");
        await svc.DeactivateAsync(TenantId, CancellationToken.None);
        repo.All[0].IsActive.Should().BeFalse();

        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "token2");

        repo.All[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetContext_returns_decrypted_context_for_active_connection()
    {
        var (svc, _, _, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "raw-token",
            refreshToken: "raw-refresh",
            expiresAt: DateTimeOffset.UtcNow.AddHours(1),
            defaultPipelineId: "pipeline-7");

        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx!.Provider.Should().Be(CrmProvider.Pipedrive);
        ctx.AuthToken.Should().Be("raw-token");
        ctx.RefreshToken.Should().Be("raw-refresh");
        ctx.DefaultPipelineId.Should().Be("pipeline-7");
    }

    [Fact]
    public async Task GetContext_returns_null_when_no_connection()
    {
        var (svc, _, _, _) = Build();
        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task GetContext_returns_null_when_connection_inactive()
    {
        var (svc, _, _, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "token");
        await svc.DeactivateAsync(TenantId, CancellationToken.None);

        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);

        ctx.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_does_not_expose_credentials()
    {
        var (svc, _, _, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "raw-token");

        var summary = await svc.GetSummaryAsync(TenantId, CancellationToken.None);

        summary.Should().NotBeNull();
        summary!.GetType().GetProperty("AuthToken").Should().BeNull();
        summary.GetType().GetProperty("EncryptedAuthToken").Should().BeNull();
        summary.Provider.Should().Be(CrmProvider.Pipedrive);
    }

    [Fact]
    public async Task SetDefaultPipelineId_fails_when_no_connection()
    {
        var (svc, _, _, _) = Build();
        var result = await svc.SetDefaultPipelineIdAsync(TenantId, "pipeline", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ContextNone_returns_provider_None_for_safe_pipeline_fallback()
    {
        var ctx = CrmConnectionContext.None(TenantId);

        ctx.Provider.Should().Be(CrmProvider.None);
        ctx.AuthToken.Should().BeEmpty();
        ctx.TenantId.Should().Be(TenantId);
    }

    private static (CrmConnectionService svc, FakeRepo repo, FakeProtector protector, FakeBrokerFactory brokers) Build()
    {
        var repo = new FakeRepo();
        var protector = new FakeProtector();
        var brokers = new FakeBrokerFactory();
        var svc = new CrmConnectionService(repo, protector, brokers, NullLogger<CrmConnectionService>.Instance);
        return (svc, repo, protector, brokers);
    }

    private class FakeRepo : ICrmConnectionRepository
    {
        public List<CrmConnection> All { get; } = new();

        public Task<CrmConnection?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.TenantId == tenantId));

        public Task<CrmConnection?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<CrmConnection>> ListRefreshableExpiringBeforeAsync(
            DateTimeOffset cutoff, CancellationToken ct)
        {
            IReadOnlyList<CrmConnection> result = All
                .Where(c => c.IsActive
                         && c.ExpiresAt.HasValue
                         && c.ExpiresAt.Value <= cutoff
                         && c.EncryptedRefreshToken is not null)
                .ToList();
            return Task.FromResult(result);
        }

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

    private class FakeBrokerFactory : ICrmOAuthBrokerFactory
    {
        public Dictionary<CrmProvider, FakeBroker> Brokers { get; } = new();

        public FakeBroker Register(CrmProvider provider)
        {
            var broker = new FakeBroker(provider);
            Brokers[provider] = broker;
            return broker;
        }

        public ICrmOAuthBroker Resolve(CrmProvider provider)
            => Brokers.TryGetValue(provider, out var b)
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
        public CrmProvider Provider { get; }
        public int RefreshCalls { get; private set; }
        public string? LastRefreshToken { get; private set; }
        public OAuthTokens? NextRefreshTokens { get; set; }
        public string? NextRefreshError { get; set; }

        public FakeBroker(CrmProvider provider) => Provider = provider;

        public string BuildAuthorizeUrl(string state, string redirectUri) =>
            $"https://example.test/authorize?state={state}";

        public Task<ServiceResult<OAuthTokens>> ExchangeAuthorizationCodeAsync(
            string code, string redirectUri, CancellationToken ct) =>
            Task.FromResult(NextRefreshTokens is null
                ? ServiceResult<OAuthTokens>.Fail("not configured")
                : ServiceResult<OAuthTokens>.Ok(NextRefreshTokens));

        public Task<ServiceResult<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            RefreshCalls++;
            LastRefreshToken = refreshToken;
            if (NextRefreshError is not null)
                return Task.FromResult(ServiceResult<OAuthTokens>.Fail(NextRefreshError));
            return Task.FromResult(ServiceResult<OAuthTokens>.Ok(
                NextRefreshTokens ?? throw new InvalidOperationException("set NextRefreshTokens")));
        }
    }

    [Fact]
    public async Task GetContext_refreshes_when_token_expired_and_broker_registered()
    {
        var (svc, repo, _, brokers) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "old-token",
            refreshToken: "old-refresh",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var broker = brokers.Register(CrmProvider.Pipedrive);
        broker.NextRefreshTokens = new OAuthTokens(
            "fresh-access", "fresh-refresh", DateTimeOffset.UtcNow.AddHours(1),
            "https://acme.pipedrive.test");

        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx!.AuthToken.Should().Be("fresh-access");
        ctx.RefreshToken.Should().Be("fresh-refresh");
        ctx.ApiBaseUrl.Should().Be("https://acme.pipedrive.test");
        broker.RefreshCalls.Should().Be(1);
        broker.LastRefreshToken.Should().Be("old-refresh");
        repo.All[0].EncryptedAuthToken.Should().Be("ENC:fresh-access");
    }

    [Fact]
    public async Task GetContext_skips_refresh_when_token_not_yet_expired()
    {
        var (svc, _, _, brokers) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "live-token",
            refreshToken: "rt",
            expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        var broker = brokers.Register(CrmProvider.Pipedrive);

        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx!.AuthToken.Should().Be("live-token");
        broker.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetContext_returns_null_when_expired_and_no_broker_registered()
    {
        var (svc, _, _, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "old",
            refreshToken: "rt",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);

        ctx.Should().BeNull();
    }

    [Fact]
    public async Task GetContext_returns_null_when_broker_refresh_fails()
    {
        var (svc, _, _, brokers) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "old",
            refreshToken: "rt",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var broker = brokers.Register(CrmProvider.Pipedrive);
        broker.NextRefreshError = "invalid_grant";

        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);

        ctx.Should().BeNull();
        broker.RefreshCalls.Should().Be(1);
    }

    [Fact]
    public async Task RefreshExpiring_refreshes_only_connections_within_window()
    {
        var (svc, repo, _, brokers) = Build();
        var nearTenant = Guid.NewGuid();
        var farTenant = Guid.NewGuid();
        await svc.ConnectAsync(nearTenant, CrmProvider.Pipedrive, "near-old",
            refreshToken: "near-rt",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(10));
        await svc.ConnectAsync(farTenant, CrmProvider.Pipedrive, "far-current",
            refreshToken: "far-rt",
            expiresAt: DateTimeOffset.UtcNow.AddHours(2));

        var broker = brokers.Register(CrmProvider.Pipedrive);
        broker.NextRefreshTokens = new OAuthTokens(
            "fresh-access", "fresh-refresh", DateTimeOffset.UtcNow.AddHours(1), null);

        var result = await svc.RefreshExpiringAsync(TimeSpan.FromMinutes(30), CancellationToken.None);

        result.Candidates.Should().Be(1);
        result.Refreshed.Should().Be(1);
        result.Failed.Should().Be(0);
        broker.RefreshCalls.Should().Be(1);
        repo.All.Single(c => c.TenantId == nearTenant).EncryptedAuthToken.Should().Be("ENC:fresh-access");
        repo.All.Single(c => c.TenantId == farTenant).EncryptedAuthToken.Should().Be("ENC:far-current");
    }

    [Fact]
    public async Task RefreshExpiring_skips_connections_without_refresh_token()
    {
        var (svc, _, _, brokers) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "access-only",
            refreshToken: null,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var broker = brokers.Register(CrmProvider.Pipedrive);

        var result = await svc.RefreshExpiringAsync(TimeSpan.FromMinutes(30), CancellationToken.None);

        result.Candidates.Should().Be(0);
        result.Refreshed.Should().Be(0);
        broker.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task RefreshExpiring_counts_broker_failure_as_failed()
    {
        var (svc, _, _, brokers) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "old",
            refreshToken: "rt",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var broker = brokers.Register(CrmProvider.Pipedrive);
        broker.NextRefreshError = "invalid_grant";

        var result = await svc.RefreshExpiringAsync(TimeSpan.FromMinutes(30), CancellationToken.None);

        result.Candidates.Should().Be(1);
        result.Refreshed.Should().Be(0);
        result.Failed.Should().Be(1);
    }

    [Fact]
    public async Task RefreshExpiring_skips_inactive_connections()
    {
        var (svc, _, _, brokers) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "old",
            refreshToken: "rt",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        await svc.DeactivateAsync(TenantId, CancellationToken.None);
        var broker = brokers.Register(CrmProvider.Pipedrive);

        var result = await svc.RefreshExpiringAsync(TimeSpan.FromMinutes(30), CancellationToken.None);

        result.Candidates.Should().Be(0);
        broker.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task RefreshExpiring_rejects_negative_window()
    {
        var (svc, _, _, _) = Build();

        Func<Task> act = () => svc.RefreshExpiringAsync(TimeSpan.FromMinutes(-1), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ConnectFromTokens_persists_token_bundle_with_api_base_url()
    {
        var (svc, repo, _, _) = Build();
        var tokens = new OAuthTokens(
            "access", "refresh", DateTimeOffset.UtcNow.AddHours(1),
            "https://acme.pipedrive.test");

        var result = await svc.ConnectFromTokensAsync(TenantId, CrmProvider.Pipedrive, tokens);

        result.IsSuccess.Should().BeTrue();
        var stored = repo.All.Single();
        stored.EncryptedAuthToken.Should().Be("ENC:access");
        stored.EncryptedRefreshToken.Should().Be("ENC:refresh");
        stored.ApiBaseUrl.Should().Be("https://acme.pipedrive.test");
    }
}
