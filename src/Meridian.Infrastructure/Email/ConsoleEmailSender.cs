using Meridian.Application.Common;
using Meridian.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Email;

public class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) => _logger = logger;

    public Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var messageId = Guid.NewGuid().ToString("N");
        _logger.LogInformation(
            "[EMAIL:{MessageId}] From: {From} <{DisplayName}> -> {To} | Subject: {Subject}\n{Body}",
            messageId, message.From, message.DisplayName, message.To, message.Subject, message.BodyHtml);
        return Task.FromResult(ServiceResult<SendResult>.Ok(new SendResult(messageId)));
    }
}
