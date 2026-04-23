using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Outreach;
using Meridian.Infrastructure.Email;
using Meridian.Infrastructure.Outreach.Resend;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Outreach;

public class TenantRoutedEmailSender : IEmailSender
{
    public const string NoConfigError = "no_outbound_config";
    public const string UnknownProviderError = "unknown_provider";

    private readonly TenantOutboundContext _context;
    private readonly ConsoleEmailSender _console;
    private readonly ResendEmailSender _resend;
    private readonly ILogger<TenantRoutedEmailSender> _logger;

    public TenantRoutedEmailSender(
        TenantOutboundContext context,
        ConsoleEmailSender console,
        ResendEmailSender resend,
        ILogger<TenantRoutedEmailSender> logger)
    {
        _context = context;
        _console = console;
        _resend = resend;
        _logger = logger;
    }

    public async Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var settings = await _context.GetAsync(ct);
        if (settings is null)
        {
            _logger.LogWarning("No enabled outbound configuration for tenant; send to {Recipient} blocked",
                message.To);
            return ServiceResult<SendResult>.Fail(NoConfigError);
        }

        var routed = message with
        {
            From = settings.FromAddress,
            DisplayName = settings.FromName
        };

        return settings.ProviderType switch
        {
            OutboundProviderType.Console => await _console.SendAsync(routed, ct),
            OutboundProviderType.Resend => await _resend.SendAsync(routed, settings.ApiKey, settings.ReplyToAddress, ct),
            _ => ServiceResult<SendResult>.Fail(UnknownProviderError)
        };
    }
}
