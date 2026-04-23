using Meridian.Domain.Outreach;

namespace Meridian.Application.Ports;

public interface IOutboundConfigurationRepository
{
    Task<OutboundConfiguration?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(OutboundConfiguration config, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
