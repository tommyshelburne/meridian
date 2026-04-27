using Meridian.Domain.Crm;

namespace Meridian.Application.Ports;

public interface ICrmConnectionRepository
{
    Task<CrmConnection?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct);
    Task<CrmConnection?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(CrmConnection connection, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
