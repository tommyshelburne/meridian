using Meridian.Application.Common;
using Meridian.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Outreach;

public class ThrottledEmailSender : IEmailSender
{
    public const string CapReachedError = "daily_cap_reached";
    public const string OutsideSendWindowError = "outside_send_window";

    private readonly IEmailSender _inner;
    private readonly SendThrottleState _state;
    private readonly SendThrottleOptions _options;
    private readonly ILogger<ThrottledEmailSender> _logger;
    private readonly Func<DateTimeOffset> _clock;

    public ThrottledEmailSender(
        IEmailSender inner,
        SendThrottleState state,
        IOptions<SendThrottleOptions> options,
        ILogger<ThrottledEmailSender> logger)
        : this(inner, state, options.Value, logger, () => DateTimeOffset.UtcNow)
    {
    }

    public ThrottledEmailSender(
        IEmailSender inner,
        SendThrottleState state,
        SendThrottleOptions options,
        ILogger<ThrottledEmailSender> logger,
        Func<DateTimeOffset> clock)
    {
        _inner = inner;
        _state = state;
        _options = options;
        _logger = logger;
        _clock = clock;
        _state.DailyCap = options.DailyCap;
    }

    public async Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var now = _clock();

        if (_options.EnforceSendWindow && !IsWithinSendWindow(now.UtcDateTime.TimeOfDay))
        {
            _logger.LogInformation("Skipping send to {Recipient}: outside send window",
                message.To);
            return ServiceResult<SendResult>.Fail(OutsideSendWindowError);
        }

        if (_state.IsCapReached)
        {
            _logger.LogWarning("Daily send cap reached ({Cap}); deferring send to {Recipient}",
                _options.DailyCap, message.To);
            return ServiceResult<SendResult>.Fail(CapReachedError);
        }

        var result = await _inner.SendAsync(message, ct);
        if (result.IsSuccess)
            _state.RecordSend();

        return result;
    }

    private bool IsWithinSendWindow(TimeSpan timeOfDay) =>
        timeOfDay >= _options.SendWindowStart && timeOfDay <= _options.SendWindowEnd;
}
