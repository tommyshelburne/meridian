using System.Text.Json;

namespace Meridian.Infrastructure.Ingestion.Generic;

public record GenericHtmlParameters(
    string Url,
    string AgencyName,
    string ItemXPath,
    GenericHtmlFieldMap FieldMap,
    string? AgencyState = null,
    bool IsDefense = false,
    string? BaseUrl = null,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public static GenericHtmlParameters? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;

        var parsed = JsonSerializer.Deserialize<GenericHtmlParameters>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.Url)
            || string.IsNullOrWhiteSpace(parsed.AgencyName)
            || string.IsNullOrWhiteSpace(parsed.ItemXPath)
            || parsed.FieldMap is null
            || string.IsNullOrWhiteSpace(parsed.FieldMap.Title))
            return null;

        return parsed;
    }
}

public record GenericHtmlFieldMap(
    string Title,
    string? ExternalId = null,
    string? ExternalIdAttribute = null,
    string? Description = null,
    string? DetailUrl = null,
    string? DetailUrlAttribute = null,
    string? PostedDate = null,
    string? ResponseDeadline = null,
    string? NaicsCode = null,
    string? EstimatedValue = null);
