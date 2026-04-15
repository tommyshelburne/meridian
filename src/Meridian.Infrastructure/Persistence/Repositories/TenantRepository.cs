using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly MeridianDbContext _db;

    public TenantRepository(MeridianDbContext db) => _db = db;

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<Tenant>> GetActiveTenantsAsync(CancellationToken ct)
        => await _db.Tenants.ToListAsync(ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct)
        => await _db.Tenants.AddAsync(tenant, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
