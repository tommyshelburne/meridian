namespace Meridian.Domain.Markets;

/// <summary>
/// One cell of the federal procurement market-size reference grid. A cell holds
/// the trailing-twelve-month total obligated federal contract dollars for a single
/// (NAICS code, place-of-performance state, set-aside category) combination.
///
/// This is GLOBAL market reference data shared across all tenants — it is NOT
/// tenant-owned and intentionally carries no TenantId. It must never receive a
/// tenant query filter (see MeridianDbContext.OnModelCreating).
/// </summary>
public class ProcurementMarketCell
{
    /// <summary>Sentinel <see cref="State"/> value meaning "nationwide / all states".</summary>
    public const string NationwideState = "US";

    public Guid Id { get; private set; }

    /// <summary>Six-digit NAICS industry code.</summary>
    public string NaicsCode { get; private set; } = null!;

    /// <summary>
    /// Two-letter US state code for place of performance, or <see cref="NationwideState"/>
    /// for the nationwide aggregate cell.
    /// </summary>
    public string State { get; private set; } = null!;

    /// <summary>Set-aside category this cell's dollars are scoped to.</summary>
    public SetAsideCategory SetAside { get; private set; }

    /// <summary>Trailing-twelve-month total obligated federal contract dollars.</summary>
    public decimal TrailingTwelveMonthObligated { get; private set; }

    /// <summary>Date the trailing-twelve-month window closes on (data freshness).</summary>
    public DateOnly AsOfDate { get; private set; }

    private ProcurementMarketCell() { }

    public static ProcurementMarketCell Create(
        string naicsCode,
        string state,
        SetAsideCategory setAside,
        decimal trailingTwelveMonthObligated,
        DateOnly asOfDate)
    {
        if (string.IsNullOrWhiteSpace(naicsCode))
            throw new ArgumentException("NAICS code is required.", nameof(naicsCode));

        var normalizedNaics = naicsCode.Trim();
        if (normalizedNaics.Length != 6 || !normalizedNaics.All(char.IsDigit))
            throw new ArgumentException("NAICS code must be exactly 6 digits.", nameof(naicsCode));

        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State is required.", nameof(state));

        var normalizedState = state.Trim().ToUpperInvariant();
        if (normalizedState.Length != 2)
            throw new ArgumentException(
                "State must be a 2-letter code (or the nationwide sentinel 'US').", nameof(state));

        if (trailingTwelveMonthObligated < 0m)
            throw new ArgumentException(
                "Trailing-twelve-month obligated dollars cannot be negative.",
                nameof(trailingTwelveMonthObligated));

        return new ProcurementMarketCell
        {
            Id = Guid.NewGuid(),
            NaicsCode = normalizedNaics,
            State = normalizedState,
            SetAside = setAside,
            TrailingTwelveMonthObligated = trailingTwelveMonthObligated,
            AsOfDate = asOfDate
        };
    }
}
