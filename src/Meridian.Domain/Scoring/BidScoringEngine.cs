using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;

namespace Meridian.Domain.Scoring;

public class BidScoringEngine
{
    private readonly ScoringConfig _config;

    public BidScoringEngine(ScoringConfig config)
    {
        _config = config;
    }

    public BidScore Score(Opportunity opportunity)
    {
        var laneFitTitle = ScoreLaneFitTitle(opportunity);
        var laneFitDesc = ScoreLaneFitDescription(opportunity);

        if (laneFitTitle == 0 && laneFitDesc == 0)
            return BidScore.Create(0, 0, 0, 0, 0, 0, 0, 0);

        var agencyTier = ScoreAgencyTier(opportunity);
        var winThemes = ScoreWinThemes(opportunity);
        var pastPerformance = 0; // v3.0: no automated past performance scoring yet
        var vehicleBonus = ScoreProcurementVehicle(opportunity);
        var seatSignal = ScoreSeatCount(opportunity);
        var recompeteBonus = ScoreRecompete(opportunity);

        return BidScore.Create(laneFitTitle, laneFitDesc, agencyTier, winThemes,
            pastPerformance, vehicleBonus, seatSignal, recompeteBonus);
    }

    private int ScoreLaneFitTitle(Opportunity opportunity)
    {
        var titleLower = opportunity.Title.ToLowerInvariant();
        return _config.TitleKeywords.Any(k => titleLower.Contains(k.ToLowerInvariant())) ? 2 : 0;
    }

    private int ScoreLaneFitDescription(Opportunity opportunity)
    {
        if (string.IsNullOrWhiteSpace(opportunity.Description)) return 0;
        var descLower = opportunity.Description.ToLowerInvariant();
        return _config.DescriptionKeywords.Any(k => descLower.Contains(k.ToLowerInvariant())) ? 1 : 0;
    }

    private int ScoreAgencyTier(Opportunity opportunity)
    {
        return opportunity.Agency.Tier switch
        {
            1 => 2,
            2 => 1,
            _ => 0
        };
    }

    private int ScoreWinThemes(Opportunity opportunity)
    {
        var text = $"{opportunity.Title} {opportunity.Description}".ToLowerInvariant();
        var score = 0;

        if (_config.WinThemeKeywords.Any(k => text.Contains(k.ToLowerInvariant())))
            score++;

        if (opportunity.IsRecompete && _config.KnownCompetitors.Any(c => text.Contains(c.ToLowerInvariant())))
            score++;

        return Math.Min(score, 2);
    }

    private int ScoreProcurementVehicle(Opportunity opportunity)
    {
        return opportunity.ProcurementVehicle switch
        {
            ProcurementVehicle.GsaSchedule or ProcurementVehicle.GsaEbuy => 2,
            ProcurementVehicle.Naspo or ProcurementVehicle.Ncpa or ProcurementVehicle.Sourcewell
                or ProcurementVehicle.StateCooperative => 1,
            _ => 0
        };
    }

    private int ScoreSeatCount(Opportunity opportunity)
    {
        return opportunity.EstimatedSeats switch
        {
            >= 100 => 2,
            >= 50 => 1,
            _ => 0
        };
    }

    private int ScoreRecompete(Opportunity opportunity)
    {
        if (!opportunity.IsRecompete) return 0;

        var text = $"{opportunity.Title} {opportunity.Description}".ToLowerInvariant();
        return _config.KnownCompetitors.Any(c => text.Contains(c.ToLowerInvariant())) ? 1 : 0;
    }
}
