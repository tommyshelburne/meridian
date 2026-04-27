using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Crm;

namespace Meridian.Unit.Application.Crm;

public class CrmConnectionServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Connect_persists_new_connection_with_encrypted_credentials()
    {
        var (svc, repo, protector) = Build();

        var result = await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "raw-token");

        result.IsSuccess.Should().BeTrue();
        repo.All.Should().ContainSingle();
        repo.All[0].EncryptedAuthToken.Should().Be("ENC:raw-token");
        protector.ProtectCalls.Should().Be(1);
    }

    [Fact]
    public async Task Connect_with_None_provider_fails()
    {
        var (svc, _, _) = Build();
        var result = await svc.ConnectAsync(TenantId, CrmProvider.None, "token");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_with_blank_token_fails()
    {
        var (svc, _, _) = Build();
        var result = await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "   ");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_when_existing_same_provider_rotates_credentials()
    {
        var (svc, repo, _) = Build();
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
        var (svc, repo, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "old-token", defaultPipelineId: "p-1");

        var result = await svc.ConnectAsync(TenantId, CrmProvider.HubSpot, "new-token");

        result.IsSuccess.Should().BeTrue();
        repo.All[0].Provider.Should().Be(CrmProvider.HubSpot);
        repo.All[0].DefaultPipelineId.Should().BeNull();
    }

    [Fact]
    public async Task Connect_reactivates_a_previously_deactivated_connection()
    {
        var (svc, repo, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "token");
        await svc.DeactivateAsync(TenantId, CancellationToken.None);
        repo.All[0].IsActive.Should().BeFalse();

        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "token2");

        repo.All[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetContext_returns_decrypted_context_for_active_connection()
    {
        var (svc, _, _) = Build();
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
        var (svc, _, _) = Build();
        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);
        ctx.Should().BeNull();
    }

    [Fact]
    public async Task GetContext_returns_null_when_connection_inactive()
    {
        var (svc, _, _) = Build();
        await svc.ConnectAsync(TenantId, CrmProvider.Pipedrive, "token");
        await svc.DeactivateAsync(TenantId, CancellationToken.None);

        var ctx = await svc.GetContextAsync(TenantId, CancellationToken.None);

        ctx.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_does_not_expose_credentials()
    {
        var (svc, _, _) = Build();
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
        var (svc, _, _) = Build();
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

    private static (CrmConnectionService svc, FakeRepo repo, FakeProtector protector) Build()
    {
        var repo = new FakeRepo();
        var protector = new FakeProtector();
        return (new CrmConnectionService(repo, protector), repo, protector);
    }

    private class FakeRepo : ICrmConnectionRepository
    {
        public List<CrmConnection> All { get; } = new();

        public Task<CrmConnection?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.TenantId == tenantId));

        public Task<CrmConnection?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.Id == id));

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
}
