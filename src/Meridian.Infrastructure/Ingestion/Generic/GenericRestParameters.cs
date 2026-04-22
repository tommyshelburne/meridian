using System.Text.Json;

namespace Meridian.Infrastructure.Ingestion.Generic;

public record GenericRestParameters(
    string Url,
    string AgencyName,
    string? AgencyState = null,
    bool IsDefense = false,
    string Method = "GET",
    IReadOnlyDictionary<string, string>? Headers = null,
    string? RequestBody = null,
    string ResultsJsonPath = "$",
    GenericRestFieldMap FieldMap = default!)
{
    public static GenericRestParameters? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;

        var parsed = JsonSerializer.Deserialize<GenericRestParameters>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.Url)
            || string.IsNullOrWhiteSpace(parsed.AgencyName)
            || parsed.FieldMap is null
            || string.IsNullOrWhiteSpace(parsed.FieldMap.ExternalId)
            || string.IsNullOrWhiteSpace(parsed.FieldMap.Title))
            return null;

        return parsed;
    }
}

public record GenericRestFieldMap(
    string ExternalId,
    string Title,
    string? Description = null,
    string? PostedDate = null,
    string? ResponseDeadline = null,
    string? NaicsCode = null,
    string? EstimatedValue = null);
