using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly MeridianDbContext _db;

    public TenantRepository(MeridianDbContext db) => _db = db;

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.ToLowerInvariant();
        return _db.Tenants.FirstOrDefaultAsync(t => t.Slug == normalized, ct);
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.ToLowerInvariant();
        return _db.Tenants.AnyAsync(t => t.Slug == normalized, ct);
    }

    public async Task<IReadOnlyList<Tenant>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToArray();
        if (idList.Length == 0) return Array.Empty<Tenant>();
        return await _db.Tenants.Where(t => idList.Contains(t.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Tenant>> GetActiveTenantsAsync(CancellationToken ct)
        => await _db.Tenants.Where(t => t.Status != TenantStatus.Suspended).ToListAsync(ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct)
        => await _db.Tenants.AddAsync(tenant, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
