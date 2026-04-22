using System.Globalization;
using System.Text.Json;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Ingestion.Generic;

public class InboundWebhookAdapter : IOpportunitySourceAdapter
{
    private readonly IWebhookIngestQueue _queue;
    private readonly ILogger<InboundWebhookAdapter> _logger;

    public SourceAdapterType AdapterType => SourceAdapterType.InboundWebhook;

    public InboundWebhookAdapter(IWebhookIngestQueue queue, ILogger<InboundWebhookAdapter> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
        SourceDefinition source, CancellationToken ct)
    {
        var parameters = InboundWebhookParameters.Parse(source.ParametersJson);
        if (parameters is null)
            return Task.FromResult(ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                "InboundWebhook parameters require 'secret', 'agencyName', and 'fieldMap' with 'externalId' and 'title'."));

        var payloads = _queue.DrainForSource(source.Id);
        var results = new List<IngestedOpportunity>();

        foreach (var payload in payloads)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload.RawJson);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        var ingested = Map(item, parameters);
                        if (ingested is not null) results.Add(ingested);
                    }
                }
                else
                {
                    var ingested = Map(root, parameters);
                    if (ingested is not null) results.Add(ingested);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse webhook payload for source {SourceId}", source.Id);
            }
        }

        _logger.LogInformation("InboundWebhook drained {PayloadCount} payloads producing {Count} opportunities for source {SourceId}",
            payloads.Count, results.Count, source.Id);

        return Task.FromResult(ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(
            (IReadOnlyList<IngestedOpportunity>)results));
    }

    private static IngestedOpportunity? Map(JsonElement item, InboundWebhookParameters parameters)
    {
        var map = parameters.FieldMap;
        var externalId = ReadString(item, map.ExternalId);
        var title = ReadString(item, map.Title);
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(title))
            return null;

        var description = ReadString(item, map.Description) ?? string.Empty;
        var postedDate = TryParseDate(ReadString(item, map.PostedDate)) ?? DateTimeOffset.UtcNow;
        var deadline = TryParseDate(ReadString(item, map.ResponseDeadline));
        var naics = ReadString(item, map.NaicsCode);
        var value = TryParseDecimal(ReadString(item, map.EstimatedValue));

        var agencyType = parameters.IsDefense
            ? AgencyType.FederalDefense
            : parameters.AgencyState is not null ? AgencyType.StateLocal : AgencyType.FederalCivilian;

        return new IngestedOpportunity(
            ExternalId: externalId,
            Title: title,
            Description: description,
            AgencyName: parameters.AgencyName,
            AgencyType: agencyType,
            AgencyState: parameters.AgencyState,
            PostedDate: postedDate,
            ResponseDeadline: deadline,
            NaicsCode: naics,
            EstimatedValue: value,
            ProcurementVehicle: null);
    }

    private static string? ReadString(JsonElement item, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.StartsWith("$.") ? path[2..] : path.StartsWith('$') ? path[1..] : path;
        if (string.IsNullOrWhiteSpace(trimmed)) return null;

        var current = item;
        foreach (var segment in trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var child))
                return null;
            current = child;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => current.GetRawText()
        };
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var result) ? result : null;
    }

    private static decimal? TryParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result : null;
    }
}
