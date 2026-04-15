using Meridian.Application.Common;

namespace Meridian.Application.Ports;

public record DetectedReply(string MessageId, string Subject, DateTimeOffset ReceivedAt, string FromAddress);

public interface IInboxMonitor
{
    Task<ServiceResult<IReadOnlyList<DetectedReply>>> CheckForRepliesAsync(DateTimeOffset since, CancellationToken ct);
}
