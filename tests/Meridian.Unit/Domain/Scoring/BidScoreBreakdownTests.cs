using FluentAssertions;
using Meridian.Domain.Scoring;

namespace Meridian.Unit.Domain.Scoring;

public class BidScoreBreakdownTests
{
    [Fact]
    public void Total_sums_all_dimensions()
    {
        var b = BidScoreBreakdown.Create(2, 1, 2, 2, 2, 2, 2, 1);
        b.Total.Should().Be(14);
    }

    [Fact]
    public void Dimensions_are_clamped_to_their_caps()
    {
        var b = BidScoreBreakdown.Create(99, 99, 99, 99, 99, 99, 99, 99);

        b.LaneTitle.Should().Be(2);
        b.LaneDescription.Should().Be(1);
        b.AgencyTier.Should().Be(2);
        b.WinThemes.Should().Be(2);
        b.PastPerformance.Should().Be(2);
        b.ProcurementVehicle.Should().Be(2);
        b.SeatCount.Should().Be(2);
        b.Recompete.Should().Be(1);
        b.Total.Should().Be(BidScoreBreakdown.MaxPossible);
    }

    [Fact]
    public void Negative_dimensions_clamp_to_zero()
    {
        var b = BidScoreBreakdown.Create(-5, -1, -2, -2, -2, -2, -2, -1);
        b.Total.Should().Be(0);
    }

    [Fact]
    public void Zero_factory_returns_empty_breakdown()
    {
        BidScoreBreakdown.Zero().Total.Should().Be(0);
    }
}
