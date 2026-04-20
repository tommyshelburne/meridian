using Meridian.Domain.Tenants;

namespace Meridian.Application.Ports;

public interface IMarketProfileRepository
{
    Task<MarketProfile?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MarketProfile>> GetForTenantAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(MarketProfile profile, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
