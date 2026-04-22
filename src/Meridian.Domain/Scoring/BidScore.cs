using Meridian.Domain.Common;

namespace Meridian.Domain.Scoring;

public class BidScore
{
    public int Total { get; private set; }
    public ScoreVerdict Verdict { get; private set; }
    public DateTimeOffset ScoredAt { get; private set; }
    public bool RecompeteDetected { get; private set; }
    public BidScoreBreakdown Breakdown { get; private set; } = null!;

    private BidScore() { }

    public static BidScore Create(BidScoreBreakdown breakdown, ScoreVerdict verdict, bool recompeteDetected = false)
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        return new BidScore
        {
            Total = breakdown.Total,
            Verdict = verdict,
            ScoredAt = DateTimeOffset.UtcNow,
            RecompeteDetected = recompeteDetected,
            Breakdown = breakdown
        };
    }

    public static BidScore Create(int total, ScoreVerdict verdict)
    {
        var clamped = Math.Clamp(total, 0, BidScoreBreakdown.MaxPossible);
        var breakdown = BidScoreBreakdown.Zero();
        var result = new BidScore
        {
            Total = clamped,
            Verdict = verdict,
            ScoredAt = DateTimeOffset.UtcNow,
            RecompeteDetected = false,
            Breakdown = breakdown
        };
        return result;
    }
}
