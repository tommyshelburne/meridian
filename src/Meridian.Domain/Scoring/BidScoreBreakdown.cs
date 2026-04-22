namespace Meridian.Domain.Scoring;

public class BidScoreBreakdown
{
    public int LaneTitle { get; private set; }
    public int LaneDescription { get; private set; }
    public int AgencyTier { get; private set; }
    public int WinThemes { get; private set; }
    public int PastPerformance { get; private set; }
    public int ProcurementVehicle { get; private set; }
    public int SeatCount { get; private set; }
    public int Recompete { get; private set; }

    public int Total => LaneTitle + LaneDescription + AgencyTier + WinThemes
                        + PastPerformance + ProcurementVehicle + SeatCount + Recompete;

    public const int MaxPossible = 14;

    private BidScoreBreakdown() { }

    public static BidScoreBreakdown Create(
        int laneTitle,
        int laneDescription,
        int agencyTier,
        int winThemes,
        int pastPerformance,
        int procurementVehicle,
        int seatCount,
        int recompete)
    {
        return new BidScoreBreakdown
        {
            LaneTitle = Math.Clamp(laneTitle, 0, 2),
            LaneDescription = Math.Clamp(laneDescription, 0, 1),
            AgencyTier = Math.Clamp(agencyTier, 0, 2),
            WinThemes = Math.Clamp(winThemes, 0, 2),
            PastPerformance = Math.Clamp(pastPerformance, 0, 2),
            ProcurementVehicle = Math.Clamp(procurementVehicle, 0, 2),
            SeatCount = Math.Clamp(seatCount, 0, 2),
            Recompete = Math.Clamp(recompete, 0, 1)
        };
    }

    public static BidScoreBreakdown Zero() => Create(0, 0, 0, 0, 0, 0, 0, 0);
}
