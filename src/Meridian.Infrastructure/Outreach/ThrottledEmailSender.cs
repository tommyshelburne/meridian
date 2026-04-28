using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
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
    private readonly ITenantContext _tenantContext;
    private readonly TenantOutboundContext _outboundContext;
    private readonly ILogger<ThrottledEmailSender> _logger;
    private readonly Func<DateTimeOffset> _clock;

    public ThrottledEmailSender(
        IEmailSender inner,
        SendThrottleState state,
        IOptions<SendThrottleOptions> options,
        ITenantContext tenantContext,
        TenantOutboundContext outboundContext,
        ILogger<ThrottledEmailSender> logger)
        : this(inner, state, options.Value, tenantContext, outboundContext, logger, () => DateTimeOffset.UtcNow)
    {
    }

    public ThrottledEmailSender(
        IEmailSender inner,
        SendThrottleState state,
        SendThrottleOptions options,
        ITenantContext tenantContext,
        TenantOutboundContext outboundContext,
        ILogger<ThrottledEmailSender> logger,
        Func<DateTimeOffset> clock)
    {
        _inner = inner;
        _state = state;
        _options = options;
        _tenantContext = tenantContext;
        _outboundContext = outboundContext;
        _logger = logger;
        _clock = clock;
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

        var tenantId = _tenantContext.TenantId;
        var settings = await _outboundContext.GetAsync(ct);
        var cap = settings?.DailyCap ?? _options.DailyCap;

        if (_state.GetSentToday(tenantId) >= cap)
        {
            _logger.LogWarning("Daily cap reached for tenant {TenantId} ({Cap}); deferring send to {Recipient}",
                tenantId, cap, message.To);
            return ServiceResult<SendResult>.Fail(CapReachedError);
        }

        var result = await _inner.SendAsync(message, ct);
        if (result.IsSuccess)
            _state.RecordSend(tenantId);

        return result;
    }

    private bool IsWithinSendWindow(TimeSpan timeOfDay) =>
        timeOfDay >= _options.SendWindowStart && timeOfDay <= _options.SendWindowEnd;
}
