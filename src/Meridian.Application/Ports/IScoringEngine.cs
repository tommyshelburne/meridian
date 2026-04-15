using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Application.Ports;

public interface IScoringEngine
{
    BidScore Score(Opportunity opportunity);
}
