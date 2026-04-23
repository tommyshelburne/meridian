using Meridian.Application.Ports;
using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class OutboundConfigurationRepository : IOutboundConfigurationRepository
{
    private readonly MeridianDbContext _db;

    public OutboundConfigurationRepository(MeridianDbContext db) => _db = db;

    public Task<OutboundConfiguration?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
        => _db.OutboundConfigurations.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task AddAsync(OutboundConfiguration config, CancellationToken ct)
        => await _db.OutboundConfigurations.AddAsync(config, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
