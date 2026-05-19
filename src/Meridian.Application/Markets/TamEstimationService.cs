using Meridian.Application.Common;
using Meridian.Application.Ports;

namespace Meridian.Application.Markets;

/// <summary>
/// Derives addressable TAM and serviceable pipeline figures for a prospective
/// tenant from the global federal procurement market-size reference grid.
/// </summary>
public class TamEstimationService
{
    // PLACEHOLDER — uncalibrated serviceability haircut; refine when
    // contract-size-band + recompete-timing filtering is added.
    private const decimal ServiceabilityHaircut = 0.15m;

    private readonly IProcurementMarketRepository _market;

    public TamEstimationService(IProcurementMarketRepository market) => _market = market;

    public async Task<ServiceResult<TamEstimate>> EstimateAsync(
        TamEstimationRequest request, CancellationToken ct)
    {
        if (request.NaicsCodes is null || request.NaicsCodes.Count == 0)
            return ServiceResult<TamEstimate>.Fail("At least one NAICS code is required.");

        var cells = await _market.GetMatchingCellsAsync(
            request.NaicsCodes,
            request.TargetStates ?? Array.Empty<string>(),
            request.SetAside,
            ct);

        // No matching market data is a valid outcome — return a successful
        // all-zero estimate rather than a failure.
        if (cells.Count == 0)
            return ServiceResult<TamEstimate>.Ok(TamEstimate.Empty);

        var addressableTam = cells.Sum(c => c.TrailingTwelveMonthObligated);
        var serviceablePipeline = addressableTam * ServiceabilityHaircut;
        var oldestAsOf = cells.Min(c => c.AsOfDate);

        return ServiceResult<TamEstimate>.Ok(new TamEstimate(
            addressableTam,
            serviceablePipeline,
            oldestAsOf,
            cells.Count));
    }
}
