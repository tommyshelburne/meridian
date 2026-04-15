using Meridian.Domain.Tenants;

namespace Meridian.Application.Ports;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Tenant>> GetActiveTenantsAsync(CancellationToken ct);
    Task AddAsync(Tenant tenant, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
