using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Crm.HubSpot;

// Wire DTOs for the HubSpot CRM v3 API. HubSpot wraps every object in a
// `{ id, properties: {...} }` envelope; search responses paginate under
// `{ total, results: [...] }`.

public class HubSpotObject
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("properties")] public Dictionary<string, string?> Properties { get; set; } = new();
}

public class HubSpotSearchResponse
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("results")] public List<HubSpotObject> Results { get; set; } = new();
}

public class HubSpotErrorEnvelope
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
}
