using Meridian.Application.Ports;
using Meridian.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class OidcConfigRepository : IOidcConfigRepository
{
    private readonly MeridianDbContext _db;

    public OidcConfigRepository(MeridianDbContext db) => _db = db;

    public Task<OidcConfig?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.OidcConfigs.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<OidcConfig?> GetByProviderKeyAsync(Guid tenantId, string providerKey, CancellationToken ct)
    {
        var key = providerKey.Trim().ToLowerInvariant();
        return _db.OidcConfigs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ProviderKey == key, ct);
    }

    public async Task<IReadOnlyList<OidcConfig>> GetForTenantAsync(Guid tenantId, CancellationToken ct)
        => await _db.OidcConfigs.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.DisplayName)
            .ToListAsync(ct);

    public async Task AddAsync(OidcConfig config, CancellationToken ct)
        => await _db.OidcConfigs.AddAsync(config, ct);

    public Task RemoveAsync(OidcConfig config, CancellationToken ct)
    {
        _db.OidcConfigs.Remove(config);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
