using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Infrastructure.Scoring;

public class ScoringEngineAdapter : IScoringEngine
{
    public BidScore Score(Opportunity opportunity) =>
        BidScore.Create(0, ScoreVerdict.NoBid);
}
