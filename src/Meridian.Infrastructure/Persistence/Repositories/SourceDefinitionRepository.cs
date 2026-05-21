using Meridian.Application.Ports;
using Meridian.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class SourceDefinitionRepository : ISourceDefinitionRepository
{
    private readonly MeridianDbContext _db;

    public SourceDefinitionRepository(MeridianDbContext db) => _db = db;

    public Task<SourceDefinition?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.SourceDefinitions.FirstOrDefaultAsync(s => s.Id == id, ct);

    // Webhook ingestion authenticates via the per-source secret, not a tenant
    // session, so it must resolve the source by its global id WITHOUT the
    // tenant query filter — an external caller carries no tenant context.
    public Task<SourceDefinition?> GetByIdAcrossTenantsAsync(Guid id, CancellationToken ct) =>
        _db.SourceDefinitions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<SourceDefinition>> GetForTenantAsync(Guid tenantId, CancellationToken ct) =>
        await _db.SourceDefinitions.Where(s => s.TenantId == tenantId).ToListAsync(ct);

    public async Task<IReadOnlyList<SourceDefinition>> GetEnabledForTenantAsync(
        Guid tenantId, CancellationToken ct) =>
        await _db.SourceDefinitions
            .Where(s => s.TenantId == tenantId && s.IsEnabled)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SourceDefinition>> GetAllEnabledAcrossTenantsAsync(
        CancellationToken ct) =>
        await _db.SourceDefinitions
            .IgnoreQueryFilters()
            .Where(s => s.IsEnabled)
            .ToListAsync(ct);

    public async Task AddAsync(SourceDefinition source, CancellationToken ct) =>
        await _db.SourceDefinitions.AddAsync(source, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
