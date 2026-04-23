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

    public async Task<Opportunity?> GetBySourceExternalIdAsync(
        Guid tenantId, Guid sourceDefinitionId, string externalId, CancellationToken ct)
        => await _db.Opportunities.FirstOrDefaultAsync(
            o => o.SourceDefinitionId == sourceDefinitionId && o.ExternalId == externalId, ct);

    public async Task<IReadOnlyList<Opportunity>> GetByStatusAsync(Guid tenantId, OpportunityStatus status, CancellationToken ct)
        => await _db.Opportunities.Where(o => o.Status == status).ToListAsync(ct);

    public async Task<IReadOnlyList<Opportunity>> GetByStatusesAsync(
        Guid tenantId, IReadOnlyCollection<OpportunityStatus> statuses, CancellationToken ct)
    {
        if (statuses.Count == 0) return Array.Empty<Opportunity>();
        return await _db.Opportunities
            .Where(o => statuses.Contains(o.Status))
            .OrderByDescending(o => o.Score!.Total)
            .ThenByDescending(o => o.PostedDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Opportunity>> GetWatchedAsync(Guid tenantId, CancellationToken ct)
        => await _db.Opportunities.Where(o => o.WatchedSince != null).ToListAsync(ct);

    public async Task<IReadOnlyList<Opportunity>> GetUnenrichedAsync(Guid tenantId, CancellationToken ct)
    {
        // Pursue/Partner verdicts that the automated enricher couldn't seed with a contact.
        // !Contacts.Any() translates to a NOT EXISTS subquery — no in-memory load.
        var verdicts = new[]
        {
            ScoreVerdict.Pursue,
            ScoreVerdict.Partner
        };
        var statuses = new[]
        {
            OpportunityStatus.Scored,
            OpportunityStatus.PendingReview,
            OpportunityStatus.Pursuing,
            OpportunityStatus.Partnering
        };

        return await _db.Opportunities
            .Where(o => statuses.Contains(o.Status)
                && o.Score != null
                && verdicts.Contains(o.Score.Verdict)
                && !o.Contacts.Any())
            .OrderByDescending(o => o.Score!.Total)
            .ThenByDescending(o => o.PostedDate)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Opportunity opportunity, CancellationToken ct)
        => await _db.Opportunities.AddAsync(opportunity, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
