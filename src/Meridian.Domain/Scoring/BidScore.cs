using Meridian.Domain.Common;

namespace Meridian.Domain.Scoring;

public class BidScore
{
    public int Total { get; private set; }
    public ScoreVerdict Verdict { get; private set; }
    public DateTimeOffset ScoredAt { get; private set; }

    private BidScore() { }

    public static BidScore Create(int total, ScoreVerdict verdict)
    {
        return new BidScore
        {
            Total = total < 0 ? 0 : total,
            Verdict = verdict,
            ScoredAt = DateTimeOffset.UtcNow
        };
    }
}
