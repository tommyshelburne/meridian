using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Application.Ports;
using Meridian.Domain.Auth;

namespace Meridian.Unit.Application.Auth;

public class OidcConfigServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static CreateOidcConfigRequest EntraRequest(string secret = "client-secret-123") =>
        new(
            ProviderKey: "entra-prod",
            Provider: OidcProvider.EntraId,
            DisplayName: "Acme SSO",
            Authority: "https://login.microsoftonline.com/abc/v2.0",
            ClientId: "client-id-123",
            ClientSecret: secret);

    [Fact]
    public async Task Create_encrypts_secret_and_persists()
    {
        var (svc, repo, protector) = Build();

        var result = await svc.CreateAsync(TenantId, EntraRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.All.Should().ContainSingle();
        repo.All[0].EncryptedClientSecret.Should().Be("ENC:client-secret-123");
        protector.ProtectCalls.Should().Be(1);
    }

    [Fact]
    public async Task Create_rejects_blank_secret()
    {
        var (svc, _, _) = Build();
        var result = await svc.CreateAsync(TenantId, EntraRequest("   "), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("secret");
    }

    [Fact]
    public async Task Create_rejects_duplicate_provider_key_for_same_tenant()
    {
        var (svc, _, _) = Build();
        await svc.CreateAsync(TenantId, EntraRequest(), CancellationToken.None);

        var second = await svc.CreateAsync(TenantId, EntraRequest(), CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Contain("entra-prod");
    }

    [Fact]
    public async Task Create_propagates_domain_validation_failure()
    {
        var (svc, _, _) = Build();
        var bad = EntraRequest() with { Authority = "not-a-url" };

        var result = await svc.CreateAsync(TenantId, bad, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_returns_config_with_decrypted_secret()
    {
        var (svc, _, _) = Build();
        await svc.CreateAsync(TenantId, EntraRequest("plaintext-xyz"), CancellationToken.None);

        var resolved = await svc.ResolveByProviderKeyAsync(TenantId, "entra-prod", CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.ClientSecret.Should().Be("plaintext-xyz");
        resolved.ProviderKey.Should().Be("entra-prod");
        resolved.DisplayName.Should().Be("Acme SSO");
    }

    [Fact]
    public async Task Resolve_returns_null_when_provider_not_found()
    {
        var (svc, _, _) = Build();
        var resolved = await svc.ResolveByProviderKeyAsync(TenantId, "missing", CancellationToken.None);
        resolved.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_returns_null_when_ciphertext_cannot_be_decrypted()
    {
        var (svc, repo, _) = Build();
        // Seed a config whose stored ciphertext won't be unprotectable by the fake.
        repo.Add(OidcConfig.Create(TenantId, "broken", OidcProvider.Generic,
            "Broken", "https://x.com", "client", "RAW-NOT-ENCRYPTED"));

        var resolved = await svc.ResolveByProviderKeyAsync(TenantId, "broken", CancellationToken.None);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task List_returns_summary_without_secret()
    {
        var (svc, _, _) = Build();
        await svc.CreateAsync(TenantId, EntraRequest(), CancellationToken.None);

        var summaries = await svc.ListForTenantAsync(TenantId, CancellationToken.None);

        summaries.Should().ContainSingle();
        var summary = summaries[0];
        summary.GetType().GetProperty("ClientSecret").Should().BeNull("summary must not expose secret");
        summary.GetType().GetProperty("EncryptedClientSecret").Should().BeNull();
        summary.IsEnabled.Should().BeTrue();
        summary.DisplayName.Should().Be("Acme SSO");
    }

    [Fact]
    public async Task Update_applies_field_changes()
    {
        var (svc, repo, _) = Build();
        var created = await svc.CreateAsync(TenantId, EntraRequest(), CancellationToken.None);

        var update = new UpdateOidcConfigRequest(
            DisplayName: "Renamed",
            Authority: "https://new.example.com/",
            ClientId: "new-client-id",
            Scopes: "openid profile",
            EmailClaim: "upn",
            NameClaim: "given_name");
        var result = await svc.UpdateAsync(created.Value, update, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var stored = repo.All.Single();
        stored.DisplayName.Should().Be("Renamed");
        stored.Authority.Should().Be("https://new.example.com");
        stored.ClientId.Should().Be("new-client-id");
        stored.EmailClaim.Should().Be("upn");
    }

    [Fact]
    public async Task RotateSecret_encrypts_new_secret()
    {
        var (svc, repo, protector) = Build();
        var created = await svc.CreateAsync(TenantId, EntraRequest("old"), CancellationToken.None);
        protector.ResetCalls();

        var result = await svc.RotateSecretAsync(created.Value, "new-secret", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.All.Single().EncryptedClientSecret.Should().Be("ENC:new-secret");
        protector.ProtectCalls.Should().Be(1);
    }

    [Fact]
    public async Task RotateSecret_rejects_blank()
    {
        var (svc, _, _) = Build();
        var created = await svc.CreateAsync(TenantId, EntraRequest(), CancellationToken.None);

        var result = await svc.RotateSecretAsync(created.Value, "  ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SetEnabled_toggles_flag()
    {
        var (svc, repo, _) = Build();
        var created = await svc.CreateAsync(TenantId, EntraRequest(), CancellationToken.None);

        await svc.SetEnabledAsync(created.Value, false, CancellationToken.None);
        repo.All.Single().IsEnabled.Should().BeFalse();

        await svc.SetEnabledAsync(created.Value, true, CancellationToken.None);
        repo.All.Single().IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_removes_config()
    {
        var (svc, repo, _) = Build();
        var created = await svc.CreateAsync(TenantId, EntraRequest(), CancellationToken.None);

        var result = await svc.DeleteAsync(created.Value, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.All.Should().BeEmpty();
    }

    private static (OidcConfigService svc, FakeRepo repo, FakeProtector protector) Build()
    {
        var repo = new FakeRepo();
        var protector = new FakeProtector();
        return (new OidcConfigService(repo, protector), repo, protector);
    }

    private class FakeRepo : IOidcConfigRepository
    {
        public List<OidcConfig> All { get; } = new();

        public void Add(OidcConfig c) => All.Add(c);

        public Task<OidcConfig?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.Id == id));

        public Task<OidcConfig?> GetByProviderKeyAsync(Guid tenantId, string providerKey, CancellationToken ct)
        {
            var key = providerKey.Trim().ToLowerInvariant();
            return Task.FromResult(All.FirstOrDefault(c => c.TenantId == tenantId && c.ProviderKey == key));
        }

        public Task<IReadOnlyList<OidcConfig>> GetForTenantAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<OidcConfig>>(All.Where(c => c.TenantId == tenantId).ToList());

        public Task AddAsync(OidcConfig config, CancellationToken ct)
        {
            All.Add(config);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(OidcConfig config, CancellationToken ct)
        {
            All.Remove(config);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }
        public void ResetCalls() => ProtectCalls = 0;

        public string Protect(string plaintext) { ProtectCalls++; return $"ENC:{plaintext}"; }
        public string Unprotect(string ciphertext) =>
            ciphertext.StartsWith("ENC:") ? ciphertext[4..] : throw new InvalidOperationException("not encrypted");
    }
}
