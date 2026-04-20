using Meridian.Domain.Tenants;

namespace Meridian.Application.Ports;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
    Task<IReadOnlyList<Tenant>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<Tenant>> GetActiveTenantsAsync(CancellationToken ct);
    Task AddAsync(Tenant tenant, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
