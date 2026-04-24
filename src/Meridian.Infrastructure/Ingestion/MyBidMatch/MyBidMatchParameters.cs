using System.Text.Json;

namespace Meridian.Infrastructure.Ingestion.MyBidMatch;

public record MyBidMatchParameters(
    string SubscriptionId,
    string BaseUrl = "https://mybidmatch.outreachsystems.com",
    string AgencyState = "UT")
{
    public static MyBidMatchParameters? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;

        var parsed = JsonSerializer.Deserialize<MyBidMatchParameters>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.SubscriptionId))
            return null;

        return parsed with
        {
            BaseUrl = string.IsNullOrWhiteSpace(parsed.BaseUrl)
                ? "https://mybidmatch.outreachsystems.com"
                : parsed.BaseUrl.TrimEnd('/'),
            AgencyState = string.IsNullOrWhiteSpace(parsed.AgencyState) ? "UT" : parsed.AgencyState
        };
    }
}
