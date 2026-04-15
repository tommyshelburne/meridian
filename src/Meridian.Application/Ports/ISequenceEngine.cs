using Meridian.Application.Common;

namespace Meridian.Application.Ports;

public interface ISequenceEngine
{
    Task<ServiceResult<int>> ProcessDueEnrollmentsAsync(Guid tenantId, CancellationToken ct);
}
