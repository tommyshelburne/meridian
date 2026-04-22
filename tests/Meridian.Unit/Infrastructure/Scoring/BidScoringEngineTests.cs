using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;
using Meridian.Infrastructure.Scoring;

namespace Meridian.Unit.Infrastructure.Scoring;

public class BidScoringEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly ScoringConfiguration _config = new();
    private readonly BidScoringEngine _engine;

    public BidScoringEngineTests()
    {
        _engine = new BidScoringEngine(_config, new SeatCountEstimator(_config));
    }

    private static Opportunity Make(
        string title = "RFP",
        string description = "",
        AgencyType agencyType = AgencyType.FederalCivilian,
        string? state = null,
        string? naics = null,
        ProcurementVehicle? vehicle = null,
        decimal? value = null)
    {
        return Opportunity.Create(TenantId, $"X-{Guid.NewGuid()}", OpportunitySource.SamGov,
            title, description,
            Agency.Create("Agency", agencyType, state),
            DateTimeOffset.UtcNow,
            naicsCode: naics,
            estimatedValue: value,
            procurementVehicle: vehicle);
    }

    [Fact]
    public void Lane_keyword_in_title_awards_2_points()
    {
        var result = _engine.Score(Make(title: "Contact Center Modernization RFP"));
        result.Score.Breakdown.LaneTitle.Should().Be(2);
    }

    [Fact]
    public void Lane_keyword_only_in_description_awards_1_point_to_description()
    {
        var result = _engine.Score(Make(title: "RFP 1234", description: "IVR replacement project"));
        result.Score.Breakdown.LaneTitle.Should().Be(0);
        result.Score.Breakdown.LaneDescription.Should().Be(1);
    }

    [Fact]
    public void No_lane_match_awards_zero()
    {
        var result = _engine.Score(Make(title: "Janitorial Services", description: "Cleaning"));
        result.Score.Breakdown.LaneTitle.Should().Be(0);
        result.Score.Breakdown.LaneDescription.Should().Be(0);
    }

    [Fact]
    public void Federal_agency_scores_tier_2()
    {
        var result = _engine.Score(Make(agencyType: AgencyType.FederalDefense));
        result.Score.Breakdown.AgencyTier.Should().Be(2);
    }

    [Fact]
    public void Prime_contractor_scores_tier_2()
    {
        var result = _engine.Score(Make(agencyType: AgencyType.PrimeContractor));
        result.Score.Breakdown.AgencyTier.Should().Be(2);
    }

    [Fact]
    public void Tier1_state_scores_tier_2()
    {
        var result = _engine.Score(Make(agencyType: AgencyType.StateLocal, state: "CA"));
        result.Score.Breakdown.AgencyTier.Should().Be(2);
    }

    [Fact]
    public void Non_tier1_state_scores_tier_1()
    {
        var result = _engine.Score(Make(agencyType: AgencyType.StateLocal, state: "WY"));
        result.Score.Breakdown.AgencyTier.Should().Be(1);
    }

    [Fact]
    public void Win_themes_keyword_awards_1_point()
    {
        var result = _engine.Score(Make(description: "Modernization initiative"));
        result.Score.Breakdown.WinThemes.Should().Be(1);
    }

    [Fact]
    public void Legacy_incumbent_adds_1_to_win_themes()
    {
        var result = _engine.Score(Make(description: "Replace existing Nuance IVR with AI-powered automation"));
        result.Score.Breakdown.WinThemes.Should().Be(2);
    }

    [Fact]
    public void Past_performance_naics_match_awards_2_points()
    {
        var result = _engine.Score(Make(naics: "561422"));
        result.Score.Breakdown.PastPerformance.Should().Be(2);
    }

    [Fact]
    public void Unrelated_naics_awards_zero_past_performance()
    {
        var result = _engine.Score(Make(naics: "111110"));
        result.Score.Breakdown.PastPerformance.Should().Be(0);
    }

    [Theory]
    [InlineData(ProcurementVehicle.OpenMarket, 0)]
    [InlineData(ProcurementVehicle.GsaSchedule, 1)]
    [InlineData(ProcurementVehicle.GsaEbuy, 2)]
    [InlineData(ProcurementVehicle.Naspo, 2)]
    [InlineData(ProcurementVehicle.Ncpa, 2)]
    [InlineData(ProcurementVehicle.Sourcewell, 2)]
    [InlineData(ProcurementVehicle.StateCooperative, 2)]
    public void Procurement_vehicle_scoring(ProcurementVehicle vehicle, int expected)
    {
        var result = _engine.Score(Make(vehicle: vehicle));
        result.Score.Breakdown.ProcurementVehicle.Should().Be(expected);
    }

    [Fact]
    public void Seat_count_100_or_more_awards_2_points()
    {
        var result = _engine.Score(Make(title: "Contact center for 250 agents"));
        result.Score.Breakdown.SeatCount.Should().Be(2);
        result.SeatEstimate.EstimatedSeats.Should().Be(250);
    }

    [Fact]
    public void Seat_count_50_to_99_awards_1_point()
    {
        var result = _engine.Score(Make(title: "RFP for 60 agents"));
        result.Score.Breakdown.SeatCount.Should().Be(1);
    }

    [Fact]
    public void Seat_count_unknown_awards_zero()
    {
        var result = _engine.Score(Make(title: "RFP", description: "No seat info"));
        result.Score.Breakdown.SeatCount.Should().Be(0);
    }

    [Fact]
    public void Recompete_keyword_sets_flag_and_awards_1_point()
    {
        var result = _engine.Score(Make(description: "Follow-on contract for existing services"));
        result.Score.RecompeteDetected.Should().BeTrue();
        result.Score.Breakdown.Recompete.Should().Be(1);
    }

    [Fact]
    public void No_recompete_keyword_awards_zero()
    {
        var result = _engine.Score(Make(description: "Brand new initiative"));
        result.Score.RecompeteDetected.Should().BeFalse();
        result.Score.Breakdown.Recompete.Should().Be(0);
    }

    [Fact]
    public void Pursue_verdict_at_threshold()
    {
        var result = _engine.Score(Make(
            title: "Contact Center Modernization with Nuance Replacement for 500 agents",
            description: "AI-powered call center transformation, follow-on contract",
            agencyType: AgencyType.FederalDefense,
            naics: "541512",
            vehicle: ProcurementVehicle.GsaEbuy));

        result.Score.Verdict.Should().Be(ScoreVerdict.Pursue);
        result.Score.Total.Should().BeGreaterThanOrEqualTo(_config.PursueThreshold);
    }

    [Fact]
    public void Partner_verdict_for_mid_score()
    {
        var result = _engine.Score(Make(
            title: "Contact Center RFP",
            description: "Modernization initiative for citizen services",
            agencyType: AgencyType.StateLocal,
            state: "WY",
            naics: "541512"));

        result.Score.Verdict.Should().Be(ScoreVerdict.Partner);
        result.Score.Total.Should().BeInRange(_config.PartnerThreshold, _config.PursueThreshold - 1);
    }

    [Fact]
    public void NoBid_verdict_for_low_score()
    {
        var result = _engine.Score(Make(title: "Janitorial RFP",
            agencyType: AgencyType.StateLocal, state: "WY"));

        result.Score.Verdict.Should().Be(ScoreVerdict.NoBid);
        result.Score.Total.Should().BeLessThan(_config.PartnerThreshold);
    }

    [Fact]
    public void Total_never_exceeds_max_possible()
    {
        var result = _engine.Score(Make(
            title: "Contact Center IVR Modernization with Nuance Replacement for 1000 agents",
            description: "AI-powered automation, recompete of legacy Avaya platform, citizen services",
            agencyType: AgencyType.FederalDefense,
            naics: "561422",
            vehicle: ProcurementVehicle.Sourcewell));

        result.Score.Total.Should().BeLessThanOrEqualTo(BidScoreBreakdown.MaxPossible);
    }

    [Fact]
    public void Score_persists_seat_estimate_to_opportunity_when_applied()
    {
        var opp = Make(title: "Contact center for 300 agents");
        var result = _engine.Score(opp);

        opp.SetSeatEstimate(result.SeatEstimate);
        opp.ApplyScore(result.Score);

        opp.EstimatedSeats.Should().Be(300);
        opp.SeatEstimateConfidence.Should().Be(SeatEstimateConfidence.High);
        opp.Score!.Breakdown.SeatCount.Should().Be(2);
    }
}
