using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class MarketProfileRepository : IMarketProfileRepository
{
    private readonly MeridianDbContext _db;

    public MarketProfileRepository(MeridianDbContext db) => _db = db;

    public Task<MarketProfile?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.MarketProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<MarketProfile>> GetForTenantAsync(Guid tenantId, CancellationToken ct)
        => await _db.MarketProfiles.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task AddAsync(MarketProfile profile, CancellationToken ct)
        => await _db.MarketProfiles.AddAsync(profile, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
