using Meridian.Domain.Common;

namespace Meridian.Domain.Scoring;

public class BidScore
{
    public int LaneFitTitle { get; private set; }
    public int LaneFitDescription { get; private set; }
    public int AgencyTier { get; private set; }
    public int WinThemes { get; private set; }
    public int PastPerformance { get; private set; }
    public int ProcurementVehicleBonus { get; private set; }
    public int SeatCountSignal { get; private set; }
    public int RecompeteBonus { get; private set; }
    public DateTimeOffset ScoredAt { get; private set; }

    public int Total => LaneFitTitle + LaneFitDescription + AgencyTier + WinThemes
                        + PastPerformance + ProcurementVehicleBonus + SeatCountSignal + RecompeteBonus;

    public ScoreVerdict Verdict => Total switch
    {
        >= 10 => ScoreVerdict.Pursue,
        >= 6 => ScoreVerdict.Partner,
        _ => ScoreVerdict.NoBid
    };

    private BidScore() { }

    public static BidScore Create(
        int laneFitTitle,
        int laneFitDescription,
        int agencyTier,
        int winThemes,
        int pastPerformance,
        int procurementVehicleBonus,
        int seatCountSignal,
        int recompeteBonus)
    {
        return new BidScore
        {
            LaneFitTitle = Clamp(laneFitTitle, 0, 2),
            LaneFitDescription = Clamp(laneFitDescription, 0, 1),
            AgencyTier = Clamp(agencyTier, 0, 2),
            WinThemes = Clamp(winThemes, 0, 2),
            PastPerformance = Clamp(pastPerformance, 0, 2),
            ProcurementVehicleBonus = Clamp(procurementVehicleBonus, 0, 2),
            SeatCountSignal = Clamp(seatCountSignal, 0, 2),
            RecompeteBonus = Clamp(recompeteBonus, 0, 1),
            ScoredAt = DateTimeOffset.UtcNow
        };
    }

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;
}
