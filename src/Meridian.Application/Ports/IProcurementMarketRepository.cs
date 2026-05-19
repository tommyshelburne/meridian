using Meridian.Domain.Markets;

namespace Meridian.Application.Ports;

/// <summary>
/// Port for reading the global procurement market-size reference grid.
/// This data is shared across all tenants and is not tenant-scoped.
/// </summary>
public interface IProcurementMarketRepository
{
    /// <summary>
    /// Fetches every market cell matching any of the supplied NAICS codes and,
    /// when <paramref name="states"/> is non-empty, any of the supplied states.
    /// An empty <paramref name="states"/> set matches all states (nationwide).
    /// </summary>
    Task<IReadOnlyList<ProcurementMarketCell>> GetMatchingCellsAsync(
        IReadOnlyCollection<string> naicsCodes,
        IReadOnlyCollection<string> states,
        SetAsideCategory setAside,
        CancellationToken ct);
}
