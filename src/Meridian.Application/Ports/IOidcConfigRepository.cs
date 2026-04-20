using Meridian.Domain.Auth;

namespace Meridian.Application.Ports;

public interface IOidcConfigRepository
{
    Task<OidcConfig?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OidcConfig?> GetByProviderKeyAsync(Guid tenantId, string providerKey, CancellationToken ct);
    Task<IReadOnlyList<OidcConfig>> GetForTenantAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(OidcConfig config, CancellationToken ct);
    Task RemoveAsync(OidcConfig config, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
