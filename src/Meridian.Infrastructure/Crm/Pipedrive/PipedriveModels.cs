using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Crm.Pipedrive;

// Wire DTOs for the Pipedrive REST API. Kept narrow — only the fields the
// adapter reads back. Pipedrive responses always carry `success` plus a
// `data` payload; failures still arrive on HTTP 200 with `success: false`.

public class PipedriveEnvelope<T>
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("data")] public T? Data { get; set; }
}

public class PipedriveSearchResult
{
    [JsonPropertyName("items")] public List<PipedriveSearchItem> Items { get; set; } = new();
}

public class PipedriveSearchItem
{
    [JsonPropertyName("item")] public PipedriveSearchItemBody Item { get; set; } = new();
}

public class PipedriveSearchItemBody
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public class PipedriveOrganization
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public class PipedriveDeal
{
    [JsonPropertyName("id")] public int Id { get; set; }
}

public class PipedriveActivity
{
    [JsonPropertyName("id")] public int Id { get; set; }
}
