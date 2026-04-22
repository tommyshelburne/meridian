using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Outreach;

public class SuppressionFilterEmailSender : IEmailSender
{
    public const string SuppressedError = "suppressed";

    private readonly IEmailSender _inner;
    private readonly IOutreachRepository _outreachRepo;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SuppressionFilterEmailSender> _logger;

    public SuppressionFilterEmailSender(
        IEmailSender inner,
        IOutreachRepository outreachRepo,
        ITenantContext tenantContext,
        ILogger<SuppressionFilterEmailSender> logger)
    {
        _inner = inner;
        _outreachRepo = outreachRepo;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (await _outreachRepo.IsSuppressedAsync(_tenantContext.TenantId, message.To, ct))
        {
            _logger.LogInformation("Suppressed send to {Recipient} (tenant {TenantId})",
                message.To, _tenantContext.TenantId);
            return ServiceResult<SendResult>.Fail(SuppressedError);
        }

        return await _inner.SendAsync(message, ct);
    }
}
