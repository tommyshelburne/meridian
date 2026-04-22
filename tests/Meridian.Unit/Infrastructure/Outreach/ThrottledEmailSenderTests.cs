using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Infrastructure.Outreach;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure.Outreach;

public class ThrottledEmailSenderTests
{
    private readonly RecordingEmailSender _inner = new();
    private readonly SendThrottleState _state = new();

    private ThrottledEmailSender Build(SendThrottleOptions options, DateTimeOffset now)
    {
        return new ThrottledEmailSender(_inner, _state, options,
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
        _state.SentToday.Should().Be(1);
    }

    [Fact]
    public async Task Returns_cap_reached_when_over_limit()
    {
        var sender = Build(WindowOff(cap: 1), DateTimeOffset.UtcNow);
        await sender.SendAsync(Msg(), CancellationToken.None);

        var result = await sender.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(ThrottledEmailSender.CapReachedError);
        _inner.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task Does_not_record_send_on_inner_failure()
    {
        var sender = Build(WindowOff(), DateTimeOffset.UtcNow);
        _inner.Behavior = _ => ServiceResult<SendResult>.Fail("boom");

        var result = await sender.SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _state.SentToday.Should().Be(0);
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
        _state.SentToday.Should().Be(0);
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
}
