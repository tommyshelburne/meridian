using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Ports;

public interface IOpportunityRepository
{
    Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Opportunity?> GetByExternalIdAsync(Guid tenantId, string externalId, CancellationToken ct);
    Task<Opportunity?> GetBySourceExternalIdAsync(
        Guid tenantId, Guid sourceDefinitionId, string externalId, CancellationToken ct);
    Task<IReadOnlyList<Opportunity>> GetByStatusAsync(Guid tenantId, OpportunityStatus status, CancellationToken ct);
    Task<IReadOnlyList<Opportunity>> GetByStatusesAsync(Guid tenantId, IReadOnlyCollection<OpportunityStatus> statuses, CancellationToken ct);
    Task<IReadOnlyList<Opportunity>> GetWatchedAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<Opportunity>> GetUnenrichedAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(Opportunity opportunity, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
