using FluentAssertions;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Outreach;

namespace Meridian.Unit.Application.Outreach;

public class OutboundConfigurationServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static UpsertOutboundRequest ResendRequest(string? apiKey = "rsk_live_123", int? dailyCap = null) =>
        new(OutboundProviderType.Resend, apiKey,
            "outreach@vendor.com", "Vendor",
            ReplyToAddress: null,
            InboundDomain: null,
            "1 Main St, City, ST 00000",
            "https://example.com/u",
            WebhookSecret: null,
            DailyCap: dailyCap);

    private static UpsertOutboundRequest ConsoleRequest(int? dailyCap = null) =>
        new(OutboundProviderType.Console, null,
            "outreach@vendor.com", "Vendor",
            null,
            null,
            "1 Main St, City, ST 00000",
            "https://example.com/u",
            null,
            DailyCap: dailyCap);

    [Fact]
    public async Task Get_summary_returns_null_when_no_config_exists()
    {
        var (svc, _, _) = Build();
        var summary = await svc.GetSummaryAsync(TenantId, CancellationToken.None);
        summary.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_creates_config_and_encrypts_api_key()
    {
        var (svc, repo, protector) = Build();

        var result = await svc.UpsertAsync(TenantId, ResendRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Stored.Should().NotBeNull();
        repo.Stored!.EncryptedApiKey.Should().Be("ENC:rsk_live_123");
        protector.ProtectCalls.Should().Be(1);
    }

    [Fact]
    public async Task Upsert_updates_existing_config_in_place()
    {
        var (svc, repo, _) = Build();
        await svc.UpsertAsync(TenantId, ResendRequest(), CancellationToken.None);
        var firstId = repo.Stored!.Id;

        await svc.UpsertAsync(TenantId, ResendRequest("rsk_live_NEW"), CancellationToken.None);

        repo.Stored.Id.Should().Be(firstId);
        repo.Stored.EncryptedApiKey.Should().Be("ENC:rsk_live_NEW");
    }

    [Fact]
    public async Task Upsert_resend_without_api_key_fails_when_creating()
    {
        var (svc, _, _) = Build();
        var result = await svc.UpsertAsync(TenantId, ResendRequest(apiKey: null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("API key is required");
    }

    [Fact]
    public async Task Upsert_resend_without_api_key_succeeds_when_one_already_stored()
    {
        var (svc, repo, _) = Build();
        await svc.UpsertAsync(TenantId, ResendRequest("rsk_first"), CancellationToken.None);

        var result = await svc.UpsertAsync(TenantId, ResendRequest(apiKey: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Stored!.EncryptedApiKey.Should().Be("ENC:rsk_first");
    }

    [Fact]
    public async Task Switching_from_console_to_resend_without_supplying_key_fails()
    {
        var (svc, _, _) = Build();
        await svc.UpsertAsync(TenantId, ConsoleRequest(), CancellationToken.None);

        var result = await svc.UpsertAsync(TenantId, ResendRequest(apiKey: null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("API key is required");
    }

    [Fact]
    public async Task Switch_to_console_does_not_require_api_key()
    {
        var (svc, repo, _) = Build();
        var result = await svc.UpsertAsync(TenantId, ConsoleRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Stored!.ProviderType.Should().Be(OutboundProviderType.Console);
    }

    [Fact]
    public async Task Webhook_secret_blank_preserves_existing_value()
    {
        var (svc, repo, _) = Build();
        await svc.UpsertAsync(TenantId, ResendRequest() with { WebhookSecret = "whsec_first" }, CancellationToken.None);
        var firstSecret = repo.Stored!.EncryptedWebhookSecret;

        await svc.UpsertAsync(TenantId, ResendRequest() with { WebhookSecret = null }, CancellationToken.None);

        repo.Stored.EncryptedWebhookSecret.Should().Be(firstSecret);
    }

    [Fact]
    public async Task Get_summary_reflects_stored_state_with_secret_presence_flags()
    {
        var (svc, _, _) = Build();
        await svc.UpsertAsync(TenantId,
            ResendRequest() with { WebhookSecret = "whsec_x", ReplyToAddress = "reply@vendor.com" },
            CancellationToken.None);

        var summary = await svc.GetSummaryAsync(TenantId, CancellationToken.None);

        summary.Should().NotBeNull();
        summary!.ProviderType.Should().Be(OutboundProviderType.Resend);
        summary.HasApiKey.Should().BeTrue();
        summary.HasWebhookSecret.Should().BeTrue();
        summary.ReplyToAddress.Should().Be("reply@vendor.com");
        summary.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Domain_validation_failure_propagates_as_service_failure()
    {
        var (svc, _, _) = Build();
        var bad = ResendRequest() with { FromAddress = "not-an-email" };

        var result = await svc.UpsertAsync(TenantId, bad, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_round_trips_inbound_domain_to_entity_and_summary()
    {
        var (svc, repo, _) = Build();

        var result = await svc.UpsertAsync(TenantId,
            ResendRequest() with { InboundDomain = "reply.meridian.app" },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Stored!.InboundDomain.Should().Be("reply.meridian.app");

        var summary = await svc.GetSummaryAsync(TenantId, CancellationToken.None);
        summary!.InboundDomain.Should().Be("reply.meridian.app");
    }

    [Fact]
    public async Task Upsert_round_trips_daily_cap_when_provided()
    {
        var (svc, repo, _) = Build();

        var result = await svc.UpsertAsync(TenantId, ResendRequest(dailyCap: 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Stored!.DailyCap.Should().Be(25);

        var summary = await svc.GetSummaryAsync(TenantId, CancellationToken.None);
        summary!.DailyCap.Should().Be(25);
    }

    [Fact]
    public async Task Upsert_clears_daily_cap_when_omitted_on_subsequent_save()
    {
        var (svc, repo, _) = Build();
        await svc.UpsertAsync(TenantId, ResendRequest(dailyCap: 25), CancellationToken.None);

        await svc.UpsertAsync(TenantId, ResendRequest(dailyCap: null), CancellationToken.None);

        repo.Stored!.DailyCap.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_rejects_zero_or_negative_daily_cap_via_domain_guard()
    {
        var (svc, _, _) = Build();

        var result = await svc.UpsertAsync(TenantId, ResendRequest(dailyCap: 0), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    private static (OutboundConfigurationService svc, FakeRepo repo, FakeProtector protector) Build()
    {
        var repo = new FakeRepo();
        var protector = new FakeProtector();
        return (new OutboundConfigurationService(repo, protector), repo, protector);
    }

    private class FakeRepo : IOutboundConfigurationRepository
    {
        public OutboundConfiguration? Stored { get; private set; }

        public Task<OutboundConfiguration?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult(Stored);

        public Task AddAsync(OutboundConfiguration config, CancellationToken ct)
        {
            Stored = config;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }
        public string Protect(string plaintext) { ProtectCalls++; return $"ENC:{plaintext}"; }
        public string Unprotect(string ciphertext) =>
            ciphertext.StartsWith("ENC:") ? ciphertext[4..] : ciphertext;
    }
}
