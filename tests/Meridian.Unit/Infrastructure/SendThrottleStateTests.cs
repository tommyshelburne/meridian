using FluentAssertions;
using Meridian.Infrastructure.Outreach;

namespace Meridian.Unit.Infrastructure;

public class SendThrottleStateTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public void Starts_at_zero_for_unknown_tenant()
    {
        var throttle = new SendThrottleState();
        throttle.GetSentToday(TenantA).Should().Be(0);
    }

    [Fact]
    public void Tracks_sends_per_tenant()
    {
        var throttle = new SendThrottleState();
        throttle.RecordSend(TenantA);
        throttle.RecordSend(TenantA);
        throttle.GetSentToday(TenantA).Should().Be(2);
    }

    [Fact]
    public void Counters_are_isolated_between_tenants()
    {
        var throttle = new SendThrottleState();
        throttle.RecordSend(TenantA);
        throttle.RecordSend(TenantA);
        throttle.RecordSend(TenantB);

        throttle.GetSentToday(TenantA).Should().Be(2);
        throttle.GetSentToday(TenantB).Should().Be(1);
    }
}
