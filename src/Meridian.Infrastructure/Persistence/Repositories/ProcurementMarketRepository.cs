using Meridian.Application.Ports;
using Meridian.Domain.Markets;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class ProcurementMarketRepository : IProcurementMarketRepository
{
    private readonly MeridianDbContext _db;

    public ProcurementMarketRepository(MeridianDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProcurementMarketCell>> GetMatchingCellsAsync(
        IReadOnlyCollection<string> naicsCodes,
        IReadOnlyCollection<string> states,
        SetAsideCategory setAside,
        CancellationToken ct)
    {
        if (naicsCodes.Count == 0)
            return Array.Empty<ProcurementMarketCell>();

        var normalizedNaics = naicsCodes
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct()
            .ToArray();

        var normalizedStates = states
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .ToArray();

        var query = _db.ProcurementMarketCells
            .Where(c => normalizedNaics.Contains(c.NaicsCode) && c.SetAside == setAside);

        // Empty state set => nationwide: match every state.
        if (normalizedStates.Length > 0)
            query = query.Where(c => normalizedStates.Contains(c.State));

        return await query.ToListAsync(ct);
    }
}
