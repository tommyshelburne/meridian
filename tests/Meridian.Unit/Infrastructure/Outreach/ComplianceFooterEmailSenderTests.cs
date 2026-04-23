using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Outreach;

namespace Meridian.Unit.Infrastructure.Outreach;

public class ComplianceFooterEmailSenderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly RecordingEmailSender _inner = new();

    private ComplianceFooterEmailSender Build(TenantOutboundSettings? settings)
    {
        var fakeRepo = new StubRepo(settings);
        var tenantContext = new StubTenantContext(TenantId);
        var protector = new PassThroughProtector();
        var context = new TenantOutboundContext(fakeRepo, tenantContext, protector);
        return new ComplianceFooterEmailSender(_inner, context);
    }

    private static TenantOutboundSettings Settings(
        string physicalAddress = "Test Co, 1 Main St, City, ST 00000",
        string unsubscribeBaseUrl = "https://example.com/u")
        => new(OutboundProviderType.Console, "", "f@x.com", "From", null,
            physicalAddress, unsubscribeBaseUrl);

    private static EmailMessage Msg(string body = "<p>Hello</p>") =>
        new("to@x.com", "from@x.com", "From", "Subject", body);

    [Fact]
    public async Task Footer_appends_tenant_physical_address()
    {
        await Build(Settings()).SendAsync(Msg(), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().Contain("Test Co, 1 Main St, City, ST 00000");
    }

    [Fact]
    public async Task Footer_includes_unsubscribe_link_with_recipient_email()
    {
        await Build(Settings()).SendAsync(Msg(), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().Contain("https://example.com/u?email=to%40x.com");
        sent.BodyHtml.Should().Contain("Unsubscribe");
    }

    [Fact]
    public async Task Footer_appends_only_once()
    {
        var sender = Build(Settings());
        await sender.SendAsync(Msg(), CancellationToken.None);
        var firstBody = _inner.Sent[0].BodyHtml;

        await sender.SendAsync(Msg(firstBody), CancellationToken.None);
        var secondBody = _inner.Sent[1].BodyHtml;

        secondBody.Should().Be(firstBody);
    }

    [Fact]
    public async Task Footer_handles_unsubscribe_url_with_existing_query_string()
    {
        await Build(Settings(unsubscribeBaseUrl: "https://example.com/u?source=email"))
            .SendAsync(Msg(), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().Contain("source=email&email=to%40x.com");
    }

    [Fact]
    public async Task No_tenant_config_passes_through_unmodified()
    {
        await Build(null).SendAsync(Msg("<p>Original</p>"), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().Be("<p>Original</p>");
    }

    [Fact]
    public async Task Original_body_is_preserved_above_footer()
    {
        await Build(Settings()).SendAsync(Msg("<p>Greetings, contact</p>"), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().StartWith("<p>Greetings, contact</p>");
    }

    private class StubRepo : IOutboundConfigurationRepository
    {
        private readonly OutboundConfiguration? _config;

        public StubRepo(TenantOutboundSettings? settings)
        {
            _config = settings is null
                ? null
                : OutboundConfiguration.Create(TenantId, settings.ProviderType,
                    settings.ProviderType == OutboundProviderType.Console ? "" : "stored-key",
                    settings.FromAddress, settings.FromName,
                    settings.PhysicalAddress, settings.UnsubscribeBaseUrl, settings.ReplyToAddress);
        }

        public Task<OutboundConfiguration?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult(_config);
        public Task AddAsync(OutboundConfiguration config, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class StubTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public StubTenantContext(Guid id) => TenantId = id;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private class PassThroughProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }
}
