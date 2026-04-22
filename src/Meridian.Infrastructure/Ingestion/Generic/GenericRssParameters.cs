using System.Text.Json;

namespace Meridian.Infrastructure.Ingestion.Generic;

public record GenericRssParameters(
    string FeedUrl,
    string AgencyName,
    string? AgencyState = null,
    bool IsDefense = false,
    IReadOnlyList<string>? IncludeKeywords = null,
    IReadOnlyList<string>? ExcludeKeywords = null)
{
    public static GenericRssParameters? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;

        var parsed = JsonSerializer.Deserialize<GenericRssParameters>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.FeedUrl) || string.IsNullOrWhiteSpace(parsed.AgencyName))
            return null;

        return parsed;
    }
}
