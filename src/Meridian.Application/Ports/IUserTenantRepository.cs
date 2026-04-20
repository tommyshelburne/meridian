using Meridian.Domain.Users;

namespace Meridian.Application.Ports;

public interface IUserTenantRepository
{
    Task<UserTenant?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<UserTenant?> GetAsync(Guid userId, Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<UserTenant>> GetForUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<UserTenant>> GetForTenantAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(UserTenant membership, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
