using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Infrastructure.Scoring;

public class BidScoringEngine : IScoringEngine
{
    private readonly ScoringConfiguration _config;
    private readonly SeatCountEstimator _seatEstimator;

    public BidScoringEngine(ScoringConfiguration config, SeatCountEstimator seatEstimator)
    {
        _config = config;
        _seatEstimator = seatEstimator;
    }

    public ScoringResult Score(Opportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(opportunity);

        var title = opportunity.Title ?? string.Empty;
        var description = opportunity.Description ?? string.Empty;

        var laneTitle = ScoreLaneTitle(title);
        var laneDesc = ScoreLaneDescription(description);
        var agencyTier = ScoreAgencyTier(opportunity.Agency);
        var winThemes = ScoreWinThemes(title, description);
        var pastPerformance = ScorePastPerformance(opportunity.NaicsCode);
        var procVehicle = ScoreProcurementVehicle(opportunity.ProcurementVehicle);

        var seatEstimate = _seatEstimator.Estimate(opportunity);
        var seatCountPoints = ScoreSeatCount(seatEstimate);

        var recompeteDetected = ContainsAny(title, _config.RecompeteKeywords)
                                 || ContainsAny(description, _config.RecompeteKeywords);
        var recompetePoints = recompeteDetected ? 1 : 0;

        var breakdown = BidScoreBreakdown.Create(
            laneTitle, laneDesc, agencyTier, winThemes,
            pastPerformance, procVehicle, seatCountPoints, recompetePoints);

        var verdict = ResolveVerdict(breakdown.Total);
        var score = BidScore.Create(breakdown, verdict, recompeteDetected);

        return new ScoringResult(score, seatEstimate);
    }

    private int ScoreLaneTitle(string title) =>
        ContainsAny(title, _config.LaneKeywords) ? 2 : 0;

    private int ScoreLaneDescription(string description) =>
        ContainsAny(description, _config.LaneKeywords) ? 1 : 0;

    private int ScoreAgencyTier(Agency agency)
    {
        return agency.Type switch
        {
            AgencyType.PrimeContractor => 2,
            AgencyType.FederalDefense => 2,
            AgencyType.FederalCivilian => 2,
            AgencyType.StateLocal when IsTier1State(agency.State) => 2,
            AgencyType.StateLocal => 1,
            _ => 0
        };
    }

    private int ScoreWinThemes(string title, string description)
    {
        var hasTheme = ContainsAny(title, _config.WinThemeKeywords)
                       || ContainsAny(description, _config.WinThemeKeywords);
        var hasLegacyIncumbent = ContainsAny(title, _config.LegacyIncumbentKeywords)
                                  || ContainsAny(description, _config.LegacyIncumbentKeywords);

        var pts = 0;
        if (hasTheme) pts += 1;
        if (hasLegacyIncumbent) pts += 1;
        return Math.Min(pts, 2);
    }

    private int ScorePastPerformance(string? naicsCode)
    {
        if (string.IsNullOrWhiteSpace(naicsCode)) return 0;
        return _config.PastPerformanceNaicsCodes.Contains(naicsCode.Trim()) ? 2 : 0;
    }

    private static int ScoreProcurementVehicle(ProcurementVehicle? vehicle)
    {
        return vehicle switch
        {
            ProcurementVehicle.GsaEbuy => 2,
            ProcurementVehicle.Naspo => 2,
            ProcurementVehicle.Ncpa => 2,
            ProcurementVehicle.Sourcewell => 2,
            ProcurementVehicle.StateCooperative => 2,
            ProcurementVehicle.GsaSchedule => 1,
            ProcurementVehicle.OpenMarket => 0,
            ProcurementVehicle.Other => 0,
            null => 0,
            _ => 0
        };
    }

    private static int ScoreSeatCount(SeatEstimate estimate)
    {
        if (estimate.EstimatedSeats is not { } seats) return 0;
        if (seats >= 100) return 2;
        if (seats >= 50) return 1;
        return 0;
    }

    private ScoreVerdict ResolveVerdict(int total)
    {
        if (total >= _config.PursueThreshold) return ScoreVerdict.Pursue;
        if (total >= _config.PartnerThreshold) return ScoreVerdict.Partner;
        return ScoreVerdict.NoBid;
    }

    private bool IsTier1State(string? state) =>
        !string.IsNullOrWhiteSpace(state) && _config.Tier1States.Contains(state.Trim().ToUpperInvariant());

    private static bool ContainsAny(string text, IEnumerable<string> needles)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
