using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class OpportunityRepository : IOpportunityRepository
{
    private readonly MeridianDbContext _db;

    public OpportunityRepository(MeridianDbContext db) => _db = db;

    public async Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Opportunities.Include(o => o.Contacts).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<Opportunity?> GetByExternalIdAsync(Guid tenantId, string externalId, CancellationToken ct)
        => await _db.Opportunities.FirstOrDefaultAsync(o => o.ExternalId == externalId, ct);

    public async Task<IReadOnlyList<Opportunity>> GetByStatusAsync(Guid tenantId, OpportunityStatus status, CancellationToken ct)
        => await _db.Opportunities.Where(o => o.Status == status).ToListAsync(ct);

    public async Task<IReadOnlyList<Opportunity>> GetWatchedAsync(Guid tenantId, CancellationToken ct)
        => await _db.Opportunities.Where(o => o.WatchedSince != null).ToListAsync(ct);

    public async Task AddAsync(Opportunity opportunity, CancellationToken ct)
        => await _db.Opportunities.AddAsync(opportunity, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
