using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Ingestion.Generic;

public class GenericRestAdapter : IOpportunitySourceAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericRestAdapter> _logger;

    public SourceAdapterType AdapterType => SourceAdapterType.GenericRest;

    public GenericRestAdapter(HttpClient httpClient, ILogger<GenericRestAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
        SourceDefinition source, CancellationToken ct)
    {
        var parameters = GenericRestParameters.Parse(source.ParametersJson);
        if (parameters is null)
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                "GenericRest parameters require 'url', 'agencyName', and 'fieldMap' with 'externalId' and 'title'.");

        JsonDocument? document;
        try
        {
            var request = new HttpRequestMessage(new HttpMethod(parameters.Method), parameters.Url);

            if (parameters.Headers is not null)
                foreach (var header in parameters.Headers)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (!string.IsNullOrEmpty(parameters.RequestBody))
                request.Content = new StringContent(parameters.RequestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            document = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch REST endpoint {Url}", parameters.Url);
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                $"REST fetch failed: {ex.Message}");
        }

        var results = new List<IngestedOpportunity>();
        using (document)
        {
            var resultsNode = ResolvePath(document.RootElement, parameters.ResultsJsonPath);
            if (resultsNode is null || resultsNode.Value.ValueKind != JsonValueKind.Array)
            {
                return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                    $"resultsJsonPath '{parameters.ResultsJsonPath}' did not resolve to a JSON array.");
            }

            foreach (var item in resultsNode.Value.EnumerateArray())
            {
                var ingested = MapItem(item, parameters);
                if (ingested is not null)
                    results.Add(ingested);
            }
        }

        _logger.LogInformation("GenericRest fetched {Count} items from {Url} for source {SourceId}",
            results.Count, parameters.Url, source.Id);

        return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(results);
    }

    private static IngestedOpportunity? MapItem(JsonElement item, GenericRestParameters parameters)
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
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var element = ResolvePath(item, path);
        if (element is null) return null;

        return element.Value.ValueKind switch
        {
            JsonValueKind.String => element.Value.GetString(),
            JsonValueKind.Number => element.Value.ToString(),
            JsonValueKind.True or JsonValueKind.False => element.Value.GetBoolean().ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.Value.GetRawText()
        };
    }

    private static JsonElement? ResolvePath(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
            return root;

        var trimmed = path.StartsWith("$.") ? path[2..] : path.StartsWith('$') ? path[1..] : path;
        if (string.IsNullOrWhiteSpace(trimmed))
            return root;

        var current = root;
        foreach (var segment in trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var child))
                return null;
            current = child;
        }
        return current;
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
