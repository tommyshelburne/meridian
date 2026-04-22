using System.Globalization;
using System.Text.RegularExpressions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Infrastructure.Scoring;

public class SeatCountEstimator
{
    private static readonly Regex ExplicitSeatPattern = new(
        @"(?:up\s+to|approximately|estimated|approx\.?)?\s*(\d{1,5})\s*(?:seats?|agents?|users?|licen[sc]es?|stations?|positions?|representatives?|reps?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ScoringConfiguration _config;

    public SeatCountEstimator(ScoringConfiguration config)
    {
        _config = config;
    }

    public SeatEstimate Estimate(Opportunity opportunity)
    {
        var explicitMatch = TryExtractExplicit(opportunity.Title) ?? TryExtractExplicit(opportunity.Description);
        if (explicitMatch is not null)
            return SeatEstimate.Create(explicitMatch.Value, SeatEstimateConfidence.High, "explicit");

        if (opportunity.EstimatedValue is { } value && value > 0 && _config.PerSeatAnnualPrice > 0)
        {
            var seats = (int)Math.Round(value / _config.PerSeatAnnualPrice, MidpointRounding.AwayFromZero);
            if (seats > 0)
                return SeatEstimate.Create(seats, SeatEstimateConfidence.Medium, "value");
        }

        if (IsFederal(opportunity.Agency.Type) && opportunity.EstimatedValue is { } v && v >= _config.FederalSeatProxyMinValue)
            return SeatEstimate.Create(_config.FederalSeatProxy, SeatEstimateConfidence.Low, "agency_proxy");

        return SeatEstimate.Unknown();
    }

    private static int? TryExtractExplicit(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int? best = null;
        foreach (Match match in ExplicitSeatPattern.Matches(text))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                continue;
            if (n <= 0 || n > 100_000) continue;
            if (best is null || n > best) best = n;
        }
        return best;
    }

    private static bool IsFederal(AgencyType type) =>
        type is AgencyType.FederalCivilian or AgencyType.FederalDefense;
}
