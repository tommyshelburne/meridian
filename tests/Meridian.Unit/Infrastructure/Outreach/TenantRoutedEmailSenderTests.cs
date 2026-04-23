using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Email;
using Meridian.Infrastructure.Outreach;
using Meridian.Infrastructure.Outreach.Resend;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure.Outreach;

public class TenantRoutedEmailSenderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EmailMessage Msg(string from = "", string display = "") =>
        new("to@dest.com", from, display, "Subject", "<p>Body</p>");

    [Fact]
    public async Task Returns_no_config_error_when_tenant_has_no_outbound_configuration()
    {
        var harness = Harness.Build(config: null);

        var result = await harness.Router.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TenantRoutedEmailSender.NoConfigError);
    }

    [Fact]
    public async Task Returns_no_config_error_when_configuration_disabled()
    {
        var config = ResendConfig();
        config.Disable();
        var harness = Harness.Build(config);

        var result = await harness.Router.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TenantRoutedEmailSender.NoConfigError);
    }

    [Fact]
    public async Task Console_provider_routes_through_console_sender_with_overridden_from()
    {
        var harness = Harness.Build(ConsoleConfig());

        var result = await harness.Router.SendAsync(Msg(from: "ignored@x.com", display: "Ignored"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Console sender returns a generated id; absence of HTTP calls confirms Resend wasn't hit.
        harness.ResendCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resend_provider_calls_resend_with_decrypted_key_and_overridden_from()
    {
        var harness = Harness.Build(ResendConfig());

        var result = await harness.Router.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.ResendCalls.Should().Be(1);
        harness.LastResendAuth.Should().Be("decrypted-rsk-live");

        var body = harness.LastResendBody!;
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("from").GetString().Should().Be("Resend Sender <resend@vendor.com>");
        doc.RootElement.GetProperty("reply_to").GetString().Should().Be("reply@vendor.com");
    }

    [Fact]
    public async Task Tenant_lookup_caches_per_scope_so_repeated_sends_hit_repo_once()
    {
        var harness = Harness.Build(ResendConfig());

        await harness.Router.SendAsync(Msg(), CancellationToken.None);
        await harness.Router.SendAsync(Msg(), CancellationToken.None);
        await harness.Router.SendAsync(Msg(), CancellationToken.None);

        harness.RepoCalls.Should().Be(1);
        harness.ResendCalls.Should().Be(3);
    }

    private static OutboundConfiguration ConsoleConfig() =>
        OutboundConfiguration.Create(TenantId, OutboundProviderType.Console, "",
            "console@vendor.com", "Console Sender",
            "Test Co, 1 Main St", "https://example.com/u");

    private static OutboundConfiguration ResendConfig() =>
        OutboundConfiguration.Create(TenantId, OutboundProviderType.Resend, "encrypted-rsk-live",
            "resend@vendor.com", "Resend Sender",
            "Test Co, 1 Main St", "https://example.com/u",
            replyToAddress: "reply@vendor.com");

    private class Harness
    {
        public TenantRoutedEmailSender Router { get; private set; } = null!;
        public int RepoCalls => _repo.Calls;
        public int ResendCalls { get; private set; }
        public string? LastResendAuth { get; private set; }
        public string? LastResendBody { get; private set; }

        private CountingRepo _repo = null!;

        public static Harness Build(OutboundConfiguration? config)
        {
            var harness = new Harness();
            var repo = new CountingRepo(config);
            harness._repo = repo;

            var tenantContext = new StubTenantContext(TenantId);
            var protector = new PrefixProtector("decrypted-");
            var context = new TenantOutboundContext(repo, tenantContext, protector);

            var resendHandler = new FakeHandler(req =>
            {
                harness.ResendCalls++;
                harness.LastResendAuth = req.Headers.Authorization?.Parameter;
                harness.LastResendBody = req.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = $"resend-{harness.ResendCalls}" }),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });
            var resend = new ResendEmailSender(
                new HttpClient(resendHandler),
                Options.Create(new ResendOptions { BaseUrl = "https://api.resend.com" }),
                NullLogger<ResendEmailSender>.Instance);

            var console = new ConsoleEmailSender(NullLogger<ConsoleEmailSender>.Instance);

            harness.Router = new TenantRoutedEmailSender(
                context, console, resend,
                NullLogger<TenantRoutedEmailSender>.Instance);

            return harness;
        }
    }

    private class StubTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public StubTenantContext(Guid id) => TenantId = id;
        public void SetTenant(Guid id) => TenantId = id;
    }

    private class PrefixProtector : ISecretProtector
    {
        private readonly string _prefix;
        public PrefixProtector(string prefix) => _prefix = prefix;
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext)
            => ciphertext.StartsWith("encrypted-") ? _prefix + ciphertext["encrypted-".Length..] : ciphertext;
    }

    private class CountingRepo : IOutboundConfigurationRepository
    {
        private readonly OutboundConfiguration? _config;
        public int Calls { get; private set; }
        public CountingRepo(OutboundConfiguration? config) => _config = config;
        public Task<OutboundConfiguration?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_config);
        }
        public Task AddAsync(OutboundConfiguration c, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
