using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Scoring;

namespace Meridian.Unit.Infrastructure.Scoring;

public class SeatCountEstimatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly SeatCountEstimator _estimator = new(new ScoringConfiguration());

    private static Opportunity Opp(string title, string desc = "", decimal? value = null,
        AgencyType type = AgencyType.FederalCivilian, string? state = null) =>
        Opportunity.Create(TenantId, "X", OpportunitySource.SamGov, title, desc,
            Agency.Create("Test Agency", type, state),
            DateTimeOffset.UtcNow, estimatedValue: value);

    [Fact]
    public void Explicit_seat_count_in_title_returns_high_confidence()
    {
        var opp = Opp("Contact Center for 250 agents");
        var est = _estimator.Estimate(opp);

        est.EstimatedSeats.Should().Be(250);
        est.Confidence.Should().Be(SeatEstimateConfidence.High);
    }

    [Fact]
    public void Explicit_seat_count_in_description_returns_high_confidence()
    {
        var opp = Opp("RFP", "Customer service ops scaling to 75 seats by Q3");
        var est = _estimator.Estimate(opp);

        est.EstimatedSeats.Should().Be(75);
        est.Confidence.Should().Be(SeatEstimateConfidence.High);
    }

    [Fact]
    public void Multiple_explicit_matches_returns_largest()
    {
        var opp = Opp("RFP", "Phase 1: 50 agents, Phase 2: 200 agents");
        var est = _estimator.Estimate(opp);

        est.EstimatedSeats.Should().Be(200);
    }

    [Fact]
    public void Up_to_phrasing_matches()
    {
        var opp = Opp("RFP", "Up to 150 representatives across centers");
        var est = _estimator.Estimate(opp);

        est.EstimatedSeats.Should().Be(150);
    }

    [Fact]
    public void Contract_value_inference_returns_medium_confidence()
    {
        var opp = Opp("RFP", "", value: 500_000m);
        var est = _estimator.Estimate(opp);

        est.EstimatedSeats.Should().Be((int)Math.Round(500_000m / (199m * 12m)));
        est.Confidence.Should().Be(SeatEstimateConfidence.Medium);
    }

    [Fact]
    public void Federal_agency_with_high_value_returns_low_confidence_proxy()
    {
        var config = new ScoringConfiguration { PerSeatAnnualPrice = 0m };
        var estimator = new SeatCountEstimator(config);
        var opp = Opp("RFP", "", value: 10_000_000m, type: AgencyType.FederalDefense);

        var est = estimator.Estimate(opp);

        est.EstimatedSeats.Should().Be(200);
        est.Confidence.Should().Be(SeatEstimateConfidence.Low);
    }

    [Fact]
    public void No_signal_returns_unknown()
    {
        var opp = Opp("RFI", "Brief opportunity description");
        var est = _estimator.Estimate(opp);

        est.EstimatedSeats.Should().BeNull();
        est.Confidence.Should().Be(SeatEstimateConfidence.Unknown);
    }

    [Fact]
    public void Explicit_takes_precedence_over_value()
    {
        var opp = Opp("Modernize for 80 seats", "", value: 10_000_000m);
        var est = _estimator.Estimate(opp);

        est.EstimatedSeats.Should().Be(80);
        est.Confidence.Should().Be(SeatEstimateConfidence.High);
    }
}
