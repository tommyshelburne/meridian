using System.Globalization;
using HtmlAgilityPack;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Ingestion.Generic;

public class GenericHtmlAdapter : IOpportunitySourceAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericHtmlAdapter> _logger;

    public SourceAdapterType AdapterType => SourceAdapterType.GenericHtml;

    public GenericHtmlAdapter(HttpClient httpClient, ILogger<GenericHtmlAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
        SourceDefinition source, CancellationToken ct)
    {
        var parameters = GenericHtmlParameters.Parse(source.ParametersJson);
        if (parameters is null)
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                "GenericHtml parameters require 'url', 'agencyName', 'itemXPath', and 'fieldMap.title'.");

        string html;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, parameters.Url);
            if (parameters.Headers is not null)
                foreach (var header in parameters.Headers)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch HTML from {Url}", parameters.Url);
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                $"HTML fetch failed: {ex.Message}");
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var itemNodes = doc.DocumentNode.SelectNodes(parameters.ItemXPath);
        if (itemNodes is null || itemNodes.Count == 0)
        {
            _logger.LogInformation("GenericHtml: itemXPath '{XPath}' matched no nodes for {Url}",
                parameters.ItemXPath, parameters.Url);
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(Array.Empty<IngestedOpportunity>());
        }

        var baseUri = ResolveBaseUri(parameters);
        var results = new List<IngestedOpportunity>();
        foreach (var node in itemNodes)
        {
            var ingested = MapNode(node, parameters, baseUri);
            if (ingested is not null)
                results.Add(ingested);
        }

        _logger.LogInformation("GenericHtml fetched {Count} items from {Url} for source {SourceId}",
            results.Count, parameters.Url, source.Id);

        return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(results);
    }

    private static IngestedOpportunity? MapNode(HtmlNode node, GenericHtmlParameters parameters, Uri? baseUri)
    {
        var map = parameters.FieldMap;

        var title = ReadField(node, map.Title);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var detailUrlRaw = ReadField(node, map.DetailUrl, map.DetailUrlAttribute);
        var detailUrl = ResolveUrl(detailUrlRaw, baseUri);

        var explicitId = ReadField(node, map.ExternalId, map.ExternalIdAttribute);
        var externalId = !string.IsNullOrWhiteSpace(explicitId)
            ? explicitId
            : detailUrl ?? StableHash(parameters.Url, title);

        var description = ReadField(node, map.Description) ?? string.Empty;
        var posted = TryParseDate(ReadField(node, map.PostedDate)) ?? DateTimeOffset.UtcNow;
        var deadline = TryParseDate(ReadField(node, map.ResponseDeadline));
        var naics = ReadField(node, map.NaicsCode);
        var value = TryParseDecimal(ReadField(node, map.EstimatedValue));

        var agencyType = parameters.IsDefense
            ? AgencyType.FederalDefense
            : parameters.AgencyState is not null ? AgencyType.StateLocal : AgencyType.FederalCivilian;

        var metadata = new Dictionary<string, string>();
        if (detailUrl is not null) metadata["detailUrl"] = detailUrl;

        return new IngestedOpportunity(
            ExternalId: externalId,
            Title: title,
            Description: description,
            AgencyName: parameters.AgencyName,
            AgencyType: agencyType,
            AgencyState: parameters.AgencyState,
            PostedDate: posted,
            ResponseDeadline: deadline,
            NaicsCode: naics,
            EstimatedValue: value,
            ProcurementVehicle: null,
            Metadata: metadata.Count == 0 ? null : metadata);
    }

    private static string? ReadField(HtmlNode root, string? xpath, string? attribute = null)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return null;

        var node = root.SelectSingleNode(xpath);
        if (node is null) return null;

        if (!string.IsNullOrWhiteSpace(attribute))
            return node.GetAttributeValue(attribute, null!) is { } attr && !string.IsNullOrWhiteSpace(attr)
                ? attr.Trim()
                : null;

        var text = HtmlEntity.DeEntitize(node.InnerText)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    private static Uri? ResolveBaseUri(GenericHtmlParameters parameters)
    {
        var baseStr = !string.IsNullOrWhiteSpace(parameters.BaseUrl) ? parameters.BaseUrl : parameters.Url;
        return Uri.TryCreate(baseStr, UriKind.Absolute, out var baseUri) ? baseUri : null;
    }

    private static string? ResolveUrl(string? raw, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return raw;
        if (baseUri is not null && Uri.TryCreate(baseUri, raw, out var rel))
            return rel.ToString();
        return raw;
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
        var cleaned = new string(value.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result : null;
    }

    private static string StableHash(string url, string title)
    {
        unchecked
        {
            var hash = 17L;
            foreach (var c in url) hash = hash * 31 + c;
            foreach (var c in title) hash = hash * 31 + c;
            return $"html-{hash:x}";
        }
    }
}
