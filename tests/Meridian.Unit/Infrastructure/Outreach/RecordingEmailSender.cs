using Meridian.Application.Common;
using Meridian.Application.Ports;

namespace Meridian.Unit.Infrastructure.Outreach;

internal class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();
    public Func<EmailMessage, ServiceResult<SendResult>>? Behavior { get; set; }

    public Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct)
    {
        Sent.Add(message);
        var result = Behavior?.Invoke(message)
                     ?? ServiceResult<SendResult>.Ok(new SendResult($"msg-{Sent.Count}"));
        return Task.FromResult(result);
    }
}
