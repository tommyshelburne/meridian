using FluentAssertions;
using Meridian.Infrastructure.Outreach;

namespace Meridian.Unit.Infrastructure;

public class SendThrottleStateTests
{
    [Fact]
    public void Starts_at_zero()
    {
        var throttle = new SendThrottleState { DailyCap = 50 };
        throttle.SentToday.Should().Be(0);
        throttle.IsCapReached.Should().BeFalse();
    }

    [Fact]
    public void Tracks_sends()
    {
        var throttle = new SendThrottleState { DailyCap = 50 };
        throttle.RecordSend();
        throttle.RecordSend();
        throttle.SentToday.Should().Be(2);
    }

    [Fact]
    public void Caps_at_daily_limit()
    {
        var throttle = new SendThrottleState { DailyCap = 3 };
        throttle.RecordSend();
        throttle.RecordSend();
        throttle.RecordSend();
        throttle.IsCapReached.Should().BeTrue();
    }

    [Fact]
    public void Not_capped_below_limit()
    {
        var throttle = new SendThrottleState { DailyCap = 3 };
        throttle.RecordSend();
        throttle.RecordSend();
        throttle.IsCapReached.Should().BeFalse();
    }
}
