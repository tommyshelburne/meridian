using FluentAssertions;
using Meridian.Application.Markets;
using Meridian.Application.Ports;
using Meridian.Domain.Markets;

namespace Meridian.Unit.Application.Markets;

public class TamEstimationServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    [Fact]
    public async Task EstimateAsync_sums_obligated_dollars_across_matched_cells()
    {
        var repo = new FakeProcurementMarketRepository(
            ProcurementMarketCell.Create("541512", "VA", SetAsideCategory.None, 4_000_000m, AsOf),
            ProcurementMarketCell.Create("541512", "MD", SetAsideCategory.None, 6_000_000m, AsOf));
        var service = new TamEstimationService(repo);

        var result = await service.EstimateAsync(
            new TamEstimationRequest(
                new[] { "541512" },
                Array.Empty<string>(),
                SetAsideCategory.None),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AddressableTam.Should().Be(10_000_000m);
        result.Value.MatchedCellCount.Should().Be(2);
    }

    [Fact]
    public async Task EstimateAsync_applies_serviceability_haircut_to_pipeline()
    {
        var repo = new FakeProcurementMarketRepository(
            ProcurementMarketCell.Create("541512", "VA", SetAsideCategory.None, 10_000_000m, AsOf));
        var service = new TamEstimationService(repo);

        var result = await service.EstimateAsync(
            new TamEstimationRequest(
                new[] { "541512" },
                Array.Empty<string>(),
                SetAsideCategory.None),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Haircut is the 0.15 placeholder constant in TamEstimationService.
        result.Value!.ServiceablePipeline.Should().Be(1_500_000m);
    }

    [Fact]
    public async Task EstimateAsync_reports_oldest_as_of_date_among_matched_cells()
    {
        var older = new DateOnly(2025, 12, 31);
        var repo = new FakeProcurementMarketRepository(
            ProcurementMarketCell.Create("541512", "VA", SetAsideCategory.None, 1m, AsOf),
            ProcurementMarketCell.Create("541512", "MD", SetAsideCategory.None, 1m, older));
        var service = new TamEstimationService(repo);

        var result = await service.EstimateAsync(
            new TamEstimationRequest(
                new[] { "541512" },
                Array.Empty<string>(),
                SetAsideCategory.None),
            CancellationToken.None);

        result.Value!.AsOfDate.Should().Be(older);
    }

    [Fact]
    public async Task EstimateAsync_no_matched_cells_returns_successful_zero_estimate()
    {
        var repo = new FakeProcurementMarketRepository();
        var service = new TamEstimationService(repo);

        var result = await service.EstimateAsync(
            new TamEstimationRequest(
                new[] { "999999" },
                Array.Empty<string>(),
                SetAsideCategory.None),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AddressableTam.Should().Be(0m);
        result.Value.ServiceablePipeline.Should().Be(0m);
        result.Value.AsOfDate.Should().BeNull();
        result.Value.MatchedCellCount.Should().Be(0);
    }

    [Fact]
    public async Task EstimateAsync_empty_naics_list_fails()
    {
        var repo = new FakeProcurementMarketRepository();
        var service = new TamEstimationService(repo);

        var result = await service.EstimateAsync(
            new TamEstimationRequest(
                Array.Empty<string>(),
                Array.Empty<string>(),
                SetAsideCategory.None),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("NAICS");
    }

    /// <summary>
    /// In-memory repository that filters seeded cells with the same semantics as
    /// the Postgres implementation (NAICS in-set, optional state in-set, set-aside
    /// equality, empty state set = nationwide).
    /// </summary>
    private sealed class FakeProcurementMarketRepository : IProcurementMarketRepository
    {
        private readonly List<ProcurementMarketCell> _cells;

        public FakeProcurementMarketRepository(params ProcurementMarketCell[] cells)
            => _cells = cells.ToList();

        public Task<IReadOnlyList<ProcurementMarketCell>> GetMatchingCellsAsync(
            IReadOnlyCollection<string> naicsCodes,
            IReadOnlyCollection<string> states,
            SetAsideCategory setAside,
            CancellationToken ct)
        {
            var naics = naicsCodes.Select(n => n.Trim()).ToHashSet();
            var stateSet = states.Select(s => s.Trim().ToUpperInvariant()).ToHashSet();

            IReadOnlyList<ProcurementMarketCell> matched = _cells
                .Where(c => naics.Contains(c.NaicsCode)
                            && c.SetAside == setAside
                            && (stateSet.Count == 0 || stateSet.Contains(c.State)))
                .ToList();

            return Task.FromResult(matched);
        }
    }
}
