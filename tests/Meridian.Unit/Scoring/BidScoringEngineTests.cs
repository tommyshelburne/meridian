using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Unit.Scoring;

public class BidScoringEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly BidScoringEngine _engine = new(ScoringConfig.KomBeaDefault);

    private Opportunity CreateOpportunity(
        string title = "General Services",
        string description = "",
        AgencyType agencyType = AgencyType.FederalCivilian,
        int agencyTier = 0,
        int? estimatedSeats = null,
        bool isRecompete = false,
        ProcurementVehicle? vehicle = null)
    {
        var agency = Agency.Create("Test Agency", agencyType, agencyTier);
        var opp = Opportunity.Create(TenantId, $"EXT-{Guid.NewGuid():N}", OpportunitySource.SamGov,
            title, description, agency, DateTimeOffset.UtcNow, procurementVehicle: vehicle);

        if (estimatedSeats.HasValue)
            opp.SetSeatEstimate(estimatedSeats.Value, SeatEstimateConfidence.Medium);
        if (isRecompete)
            opp.MarkRecompete();

        return opp;
    }

    [Fact]
    public void No_lane_fit_returns_zero_score()
    {
        var opp = CreateOpportunity("Janitorial Services", "Cleaning supplies procurement");
        var score = _engine.Score(opp);

        score.Total.Should().Be(0);
        score.Verdict.Should().Be(ScoreVerdict.NoBid);
    }

    [Fact]
    public void Title_lane_fit_scores_two_points()
    {
        var opp = CreateOpportunity("Contact Center Support Services");
        var score = _engine.Score(opp);

        score.LaneFitTitle.Should().Be(2);
    }

    [Fact]
    public void Description_only_lane_fit_scores_one_point()
    {
        var opp = CreateOpportunity("IT Support Services", "Includes contact center and helpdesk operations");
        var score = _engine.Score(opp);

        score.LaneFitTitle.Should().Be(0);
        score.LaneFitDescription.Should().Be(1);
    }

    [Fact]
    public void Tier_1_agency_scores_two_points()
    {
        var opp = CreateOpportunity("Contact Center Services", agencyTier: 1);
        var score = _engine.Score(opp);

        score.AgencyTier.Should().Be(2);
    }

    [Fact]
    public void Tier_2_agency_scores_one_point()
    {
        var opp = CreateOpportunity("Contact Center Services", agencyTier: 2);
        var score = _engine.Score(opp);

        score.AgencyTier.Should().Be(1);
    }

    [Fact]
    public void High_seat_count_scores_two_points()
    {
        var opp = CreateOpportunity("Contact Center Services", estimatedSeats: 150);
        var score = _engine.Score(opp);

        score.SeatCountSignal.Should().Be(2);
    }

    [Fact]
    public void Medium_seat_count_scores_one_point()
    {
        var opp = CreateOpportunity("Contact Center Services", estimatedSeats: 75);
        var score = _engine.Score(opp);

        score.SeatCountSignal.Should().Be(1);
    }

    [Fact]
    public void Low_seat_count_scores_zero()
    {
        var opp = CreateOpportunity("Contact Center Services", estimatedSeats: 20);
        var score = _engine.Score(opp);

        score.SeatCountSignal.Should().Be(0);
    }

    [Fact]
    public void Recompete_with_known_competitor_scores_bonus()
    {
        var opp = CreateOpportunity("Contact Center Services",
            "Recompete of existing Nuance IVR platform", isRecompete: true);
        var score = _engine.Score(opp);

        score.RecompeteBonus.Should().Be(1);
    }

    [Fact]
    public void Recompete_without_known_competitor_scores_zero_bonus()
    {
        var opp = CreateOpportunity("Contact Center Services",
            "Recompete of existing contract", isRecompete: true);
        var score = _engine.Score(opp);

        score.RecompeteBonus.Should().Be(0);
    }

    [Fact]
    public void GSA_vehicle_scores_two_points()
    {
        var opp = CreateOpportunity("Contact Center Services", vehicle: ProcurementVehicle.GsaSchedule);
        var score = _engine.Score(opp);

        score.ProcurementVehicleBonus.Should().Be(2);
    }

    [Fact]
    public void Pursue_verdict_at_threshold()
    {
        // Title fit (2) + Tier 1 (2) + GSA (2) + 100+ seats (2) + recompete w/ competitor (1) + win theme (1) = 10
        var opp = CreateOpportunity(
            "Contact Center Modernization Services",
            "Cloud migration of legacy Nuance IVR platform",
            agencyTier: 1, estimatedSeats: 150, isRecompete: true,
            vehicle: ProcurementVehicle.GsaSchedule);
        var score = _engine.Score(opp);

        score.Total.Should().BeGreaterThanOrEqualTo(10);
        score.Verdict.Should().Be(ScoreVerdict.Pursue);
    }

    [Fact]
    public void Partner_verdict_in_range()
    {
        // Title fit (2) + Tier 2 (1) + mid seats (1) + state coop (1) + win theme (1) = 6
        var opp = CreateOpportunity(
            "Contact Center Services",
            "Digital transformation of customer experience",
            agencyTier: 2, estimatedSeats: 75,
            vehicle: ProcurementVehicle.StateCooperative);
        var score = _engine.Score(opp);

        score.Total.Should().BeInRange(6, 9);
        score.Verdict.Should().Be(ScoreVerdict.Partner);
    }

    [Fact]
    public void Max_possible_score_is_fourteen()
    {
        var maxScore = BidScore.Create(2, 1, 2, 2, 2, 2, 2, 1);
        maxScore.Total.Should().Be(14);
    }
}
