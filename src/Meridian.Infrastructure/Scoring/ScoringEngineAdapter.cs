using Meridian.Application.Ports;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Infrastructure.Scoring;

public class ScoringEngineAdapter : IScoringEngine
{
    private readonly BidScoringEngine _engine;

    public ScoringEngineAdapter(BidScoringEngine engine)
    {
        _engine = engine;
    }

    public BidScore Score(Opportunity opportunity) => _engine.Score(opportunity);
}
