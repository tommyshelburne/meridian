using Meridian.Application.Common;
using Meridian.Domain.Memory;

namespace Meridian.Application.Ports;

public interface IRagStore
{
    Task<ServiceResult<RagMemory>> StoreAsync(Guid tenantId, string entityType, Guid entityId,
        string content, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<RagMemory>>> SearchSimilarAsync(Guid tenantId, string query,
        int topK, CancellationToken ct);
}
