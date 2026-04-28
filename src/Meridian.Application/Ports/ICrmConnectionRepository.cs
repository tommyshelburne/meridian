using Meridian.Domain.Crm;

namespace Meridian.Application.Ports;

public interface ICrmConnectionRepository
{
    Task<CrmConnection?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct);
    Task<CrmConnection?> GetByIdAsync(Guid id, CancellationToken ct);

    // Cross-tenant scan for the proactive token refresh job. Returns active
    // connections whose access token expires before `cutoff` and that carry a
    // refresh token (the only ones we can renew without operator intervention).
    Task<IReadOnlyList<CrmConnection>> ListRefreshableExpiringBeforeAsync(
        DateTimeOffset cutoff, CancellationToken ct);

    Task AddAsync(CrmConnection connection, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
