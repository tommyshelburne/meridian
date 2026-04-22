using System.Text.Json;

namespace Meridian.Infrastructure.Ingestion.UsaSpending;

public record UsaSpendingParameters(
    IReadOnlyList<string> NaicsCodes,
    IReadOnlyList<string>? Keywords = null,
    decimal? MinAwardAmount = null,
    int LookbackDays = 30)
{
    public static UsaSpendingParameters Parse(string? json, UsaSpendingOptions fallback)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new UsaSpendingParameters(Array.Empty<string>(), LookbackDays: fallback.DefaultLookbackDays);

        var parsed = JsonSerializer.Deserialize<UsaSpendingParameters>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null)
            return new UsaSpendingParameters(Array.Empty<string>(), LookbackDays: fallback.DefaultLookbackDays);

        return parsed with { NaicsCodes = parsed.NaicsCodes ?? Array.Empty<string>() };
    }
}
