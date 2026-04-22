using Meridian.Domain.Common;

namespace Meridian.Domain.Scoring;

public class SeatEstimate
{
    public int? EstimatedSeats { get; private set; }
    public SeatEstimateConfidence Confidence { get; private set; }
    public string? Source { get; private set; }

    private SeatEstimate() { }

    public static SeatEstimate Create(int? estimatedSeats, SeatEstimateConfidence confidence, string? source = null)
    {
        return new SeatEstimate
        {
            EstimatedSeats = estimatedSeats is < 0 ? 0 : estimatedSeats,
            Confidence = confidence,
            Source = source
        };
    }

    public static SeatEstimate Unknown() => Create(null, SeatEstimateConfidence.Unknown);
}
