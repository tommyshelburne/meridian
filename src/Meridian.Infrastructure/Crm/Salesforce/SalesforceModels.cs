using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Crm.Salesforce;

// Salesforce REST API uses lowercase keys on write envelopes (id/success/errors)
// and the sobject field names (Id/Name/...) on query results.

public class SalesforceCreateResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("errors")] public List<SalesforceError> Errors { get; set; } = new();
}

public class SalesforceQueryResponse
{
    [JsonPropertyName("totalSize")] public int TotalSize { get; set; }
    [JsonPropertyName("done")] public bool Done { get; set; }
    [JsonPropertyName("records")] public List<SalesforceQueryRecord> Records { get; set; } = new();
}

public class SalesforceQueryRecord
{
    [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
}

public class SalesforceError
{
    [JsonPropertyName("statusCode")] public string? StatusCode { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("fields")] public List<string>? Fields { get; set; }
}
