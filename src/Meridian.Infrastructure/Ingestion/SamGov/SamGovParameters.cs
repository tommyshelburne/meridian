using System.Text.Json;

namespace Meridian.Infrastructure.Ingestion.SamGov;

public record SamGovParameters(
    IReadOnlyList<string> Keywords,
    int LookbackDays = 7)
{
    public static SamGovParameters Parse(string? json, SamGovOptions fallback)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new SamGovParameters(fallback.Keywords, fallback.LookbackDays);

        var parsed = JsonSerializer.Deserialize<SamGovParameters>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null || parsed.Keywords is null || parsed.Keywords.Count == 0)
            return new SamGovParameters(fallback.Keywords, fallback.LookbackDays);

        return parsed;
    }
}
