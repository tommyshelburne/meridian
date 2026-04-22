using System.Text.Json;

namespace Meridian.Infrastructure.Ingestion.Generic;

public record InboundWebhookParameters(
    string Secret,
    string AgencyName,
    string? AgencyState = null,
    bool IsDefense = false,
    GenericRestFieldMap FieldMap = default!)
{
    public static InboundWebhookParameters? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;

        var parsed = JsonSerializer.Deserialize<InboundWebhookParameters>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.Secret)
            || string.IsNullOrWhiteSpace(parsed.AgencyName)
            || parsed.FieldMap is null
            || string.IsNullOrWhiteSpace(parsed.FieldMap.ExternalId)
            || string.IsNullOrWhiteSpace(parsed.FieldMap.Title))
            return null;

        return parsed;
    }
}
