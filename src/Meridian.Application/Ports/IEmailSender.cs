using Meridian.Application.Common;

namespace Meridian.Application.Ports;

public record EmailMessage(string To, string From, string DisplayName, string Subject, string BodyHtml);

public record SendResult(string? MessageId);

public interface IEmailSender
{
    Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct);
}
