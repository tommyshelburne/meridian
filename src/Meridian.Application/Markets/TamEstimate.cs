namespace Meridian.Application.Markets;

/// <summary>
/// Result of a TAM estimation: the two market-size figures used by the
/// pricing-audit feature, plus provenance metadata.
/// </summary>
/// <param name="AddressableTam">
/// Total addressable market — sum of trailing-twelve-month obligated federal
/// contract dollars across every matched market cell.
/// </param>
/// <param name="ServiceablePipeline">
/// Serviceable pipeline — the addressable TAM after applying the serviceability
/// haircut.
/// </param>
/// <param name="AsOfDate">
/// Oldest <c>AsOfDate</c> among matched cells (worst-case data freshness), or
/// null when no cells matched.
/// </param>
/// <param name="MatchedCellCount">Number of market cells that fed the estimate.</param>
public sealed record TamEstimate(
    decimal AddressableTam,
    decimal ServiceablePipeline,
    DateOnly? AsOfDate,
    int MatchedCellCount)
{
    /// <summary>An all-zero estimate for the case where no market cells matched.</summary>
    public static TamEstimate Empty => new(0m, 0m, null, 0);
}
