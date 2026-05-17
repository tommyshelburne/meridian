using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Outreach;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure.Outreach;

public class ThrottledEmailSenderTests
{
    private readonly RecordingEmailSender _inner = new();
    private readonly SendThrottleState _state = new();
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private ThrottledEmailSender Build(SendThrottleOptions options, DateTimeOffset now,
        Guid? tenantId = null, int? perTenantCap = null)
    {
        var tenantContext = new FakeTenantContext { TenantId = tenantId ?? TenantA };
        var outboundContext = new FakeOutboundContext(perTenantCap);
        return new ThrottledEmailSender(_inner, _state, options, tenantContext, outboundContext,
            NullLogger<ThrottledEmailSender>.Instance, () => now);
    }

    private static EmailMessage Msg() =>
        new("to@x.com", "from@x.com", "From", "Subject", "<p>Body</p>");

    private static SendThrottleOptions WindowOff(int cap = 50) =>
        new() { DailyCap = cap, EnforceSendWindow = false };

    [Fact]
    public async Task Sends_when_under_cap_and_records()
    {
        var sender = Build(WindowOff(), DateTimeOffset.UtcNow);

        var result = await sender.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _inner.Sent.Should().HaveCount(1);
        _state.GetSentToday(TenantA).Should().Be(1);
    }

    [Fact]
    public async Task Returns_cap_reached_when_over_global_default()
    {
        var sender = Build(WindowOff(cap: 1), DateTimeOffset.UtcNow);
        await sender.SendAsync(Msg(), CancellationToken.None);

        var result = await sender.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(ThrottledEmailSender.CapReachedError);
        _inner.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task Per_tenant_cap_overrides_global_default()
    {
        // Global default would let through; per-tenant cap of 1 stops at 1.
        var sender = Build(WindowOff(cap: 50), DateTimeOffset.UtcNow, perTenantCap: 1);
        await sender.SendAsync(Msg(), CancellationToken.None);

        var result = await sender.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(ThrottledEmailSender.CapReachedError);
        _inner.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task Tenants_have_isolated_counters()
    {
        // Cap of 1, tenant A hits the cap, tenant B should still be allowed
        // because the counter is keyed per tenant.
        var senderA = Build(WindowOff(cap: 1), DateTimeOffset.UtcNow, tenantId: TenantA);
        var senderB = Build(WindowOff(cap: 1), DateTimeOffset.UtcNow, tenantId: TenantB);

        var firstA = await senderA.SendAsync(Msg(), CancellationToken.None);
        var secondA = await senderA.SendAsync(Msg(), CancellationToken.None);
        var firstB = await senderB.SendAsync(Msg(), CancellationToken.None);

        firstA.IsSuccess.Should().BeTrue();
        secondA.IsSuccess.Should().BeFalse();
        secondA.Error.Should().Be(ThrottledEmailSender.CapReachedError);
        firstB.IsSuccess.Should().BeTrue();
        _state.GetSentToday(TenantA).Should().Be(1);
        _state.GetSentToday(TenantB).Should().Be(1);
    }

    [Fact]
    public async Task Does_not_record_send_on_inner_failure()
    {
        var sender = Build(WindowOff(), DateTimeOffset.UtcNow);
        _inner.Behavior = _ => ServiceResult<SendResult>.Fail("boom");

        var result = await sender.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _state.GetSentToday(TenantA).Should().Be(0);
    }

    [Fact]
    public async Task Returns_outside_window_when_send_window_enforced()
    {
        var options = new SendThrottleOptions
        {
            DailyCap = 50,
            EnforceSendWindow = true,
            SendWindowStart = TimeSpan.FromHours(13),
            SendWindowEnd = TimeSpan.FromHours(22)
        };
        var early = new DateTimeOffset(2026, 4, 22, 6, 0, 0, TimeSpan.Zero);
        var sender = Build(options, early);

        var result = await sender.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(ThrottledEmailSender.OutsideSendWindowError);
        _inner.Sent.Should().BeEmpty();
        _state.GetSentToday(TenantA).Should().Be(0);
    }

    [Fact]
    public async Task Sends_when_within_window()
    {
        var options = new SendThrottleOptions
        {
            DailyCap = 50,
            EnforceSendWindow = true,
            SendWindowStart = TimeSpan.FromHours(13),
            SendWindowEnd = TimeSpan.FromHours(22)
        };
        var midday = new DateTimeOffset(2026, 4, 22, 17, 0, 0, TimeSpan.Zero);
        var sender = Build(options, midday);

        var result = await sender.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _inner.Sent.Should().HaveCount(1);
    }

    private class FakeTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private class FakeOutboundContext : TenantOutboundContext
    {
        private readonly int? _capOverride;
        public FakeOutboundContext(int? capOverride) : base(null!, null!, null!, null!) =>
            _capOverride = capOverride;

        public override Task<TenantOutboundSettings?> GetAsync(CancellationToken ct) =>
            Task.FromResult<TenantOutboundSettings?>(_capOverride is null
                ? null
                : new TenantOutboundSettings(
                    OutboundProviderType.Console, "", "from@x.com", "From",
                    null, "addr", "https://u/x", _capOverride));
    }
}
