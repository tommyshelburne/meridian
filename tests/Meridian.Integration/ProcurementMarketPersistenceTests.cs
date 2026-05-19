using FluentAssertions;
using Meridian.Domain.Markets;
using Meridian.Infrastructure.Persistence.Repositories;

namespace Meridian.Integration;

public class ProcurementMarketPersistenceTests
{
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    [Fact]
    public async Task GetMatchingCellsAsync_filters_by_naics_state_and_set_aside()
    {
        using var fx = new IntegrationTestFixture();

        await using (var db = fx.NewDbContext())
        {
            db.ProcurementMarketCells.AddRange(
                ProcurementMarketCell.Create("541512", "VA", SetAsideCategory.None, 5_000_000m, AsOf),
                ProcurementMarketCell.Create("541512", "MD", SetAsideCategory.None, 3_000_000m, AsOf),
                ProcurementMarketCell.Create("541512", "VA", SetAsideCategory.EightA, 1_000_000m, AsOf),
                ProcurementMarketCell.Create("236220", "VA", SetAsideCategory.None, 9_000_000m, AsOf));
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var repo = new ProcurementMarketRepository(db);
            var cells = await repo.GetMatchingCellsAsync(
                new[] { "541512" },
                new[] { "VA" },
                SetAsideCategory.None,
                CancellationToken.None);

            cells.Should().ContainSingle()
                .Which.TrailingTwelveMonthObligated.Should().Be(5_000_000m);
        }
    }

    [Fact]
    public async Task GetMatchingCellsAsync_empty_state_set_matches_all_states()
    {
        using var fx = new IntegrationTestFixture();

        await using (var db = fx.NewDbContext())
        {
            db.ProcurementMarketCells.AddRange(
                ProcurementMarketCell.Create("541512", "VA", SetAsideCategory.None, 5_000_000m, AsOf),
                ProcurementMarketCell.Create("541512", "MD", SetAsideCategory.None, 3_000_000m, AsOf),
                ProcurementMarketCell.Create("541512", "CA", SetAsideCategory.None, 2_000_000m, AsOf));
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var repo = new ProcurementMarketRepository(db);
            var cells = await repo.GetMatchingCellsAsync(
                new[] { "541512" },
                Array.Empty<string>(),
                SetAsideCategory.None,
                CancellationToken.None);

            cells.Should().HaveCount(3);
            cells.Sum(c => c.TrailingTwelveMonthObligated).Should().Be(10_000_000m);
        }
    }

    [Fact]
    public async Task GetMatchingCellsAsync_matches_multiple_naics_codes()
    {
        using var fx = new IntegrationTestFixture();

        await using (var db = fx.NewDbContext())
        {
            db.ProcurementMarketCells.AddRange(
                ProcurementMarketCell.Create("541512", "VA", SetAsideCategory.None, 5_000_000m, AsOf),
                ProcurementMarketCell.Create("236220", "VA", SetAsideCategory.None, 9_000_000m, AsOf),
                ProcurementMarketCell.Create("999999", "VA", SetAsideCategory.None, 1_000_000m, AsOf));
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var repo = new ProcurementMarketRepository(db);
            var cells = await repo.GetMatchingCellsAsync(
                new[] { "541512", "236220" },
                new[] { "VA" },
                SetAsideCategory.None,
                CancellationToken.None);

            cells.Should().HaveCount(2);
            cells.Select(c => c.NaicsCode).Should().BeEquivalentTo(new[] { "541512", "236220" });
        }
    }

    [Fact]
    public async Task ProcurementMarketCell_round_trips_and_is_not_tenant_filtered()
    {
        using var fx = new IntegrationTestFixture();

        await using (var db = fx.NewDbContext())
        {
            db.ProcurementMarketCells.Add(
                ProcurementMarketCell.Create("541512", "VA", SetAsideCategory.Sdvosb, 7_500_000m, AsOf));
            await db.SaveChangesAsync();
        }

        // Set an arbitrary tenant context — global reference data must still be
        // visible because the entity carries no tenant query filter.
        fx.TenantContext.SetTenant(Guid.NewGuid());

        await using (var db = fx.NewDbContext())
        {
            var repo = new ProcurementMarketRepository(db);
            var cells = await repo.GetMatchingCellsAsync(
                new[] { "541512" },
                Array.Empty<string>(),
                SetAsideCategory.Sdvosb,
                CancellationToken.None);

            cells.Should().ContainSingle();
            var cell = cells[0];
            cell.NaicsCode.Should().Be("541512");
            cell.State.Should().Be("VA");
            cell.SetAside.Should().Be(SetAsideCategory.Sdvosb);
            cell.TrailingTwelveMonthObligated.Should().Be(7_500_000m);
            cell.AsOfDate.Should().Be(AsOf);
        }
    }
}
