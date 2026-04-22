using System.Globalization;
using System.Xml.Linq;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Ingestion.Generic;

public class GenericRssAdapter : IOpportunitySourceAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericRssAdapter> _logger;

    public SourceAdapterType AdapterType => SourceAdapterType.GenericRss;

    public GenericRssAdapter(HttpClient httpClient, ILogger<GenericRssAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
        SourceDefinition source, CancellationToken ct)
    {
        var parameters = GenericRssParameters.Parse(source.ParametersJson);
        if (parameters is null)
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                "GenericRss parameters require 'feedUrl' and 'agencyName'.");

        string xml;
        try
        {
            var response = await _httpClient.GetAsync(parameters.FeedUrl, ct);
            response.EnsureSuccessStatusCode();
            xml = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch RSS feed {Url}", parameters.FeedUrl);
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                $"RSS fetch failed: {ex.Message}");
        }

        var results = new List<IngestedOpportunity>();
        try
        {
            var doc = XDocument.Parse(xml);
            var items = doc.Descendants("item").Concat(doc.Descendants(AtomNs + "entry"));

            foreach (var item in items)
            {
                var ingested = MapItem(item, parameters);
                if (ingested is not null && MatchesFilters(ingested, parameters))
                    results.Add(ingested);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse RSS feed {Url}", parameters.FeedUrl);
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                $"RSS parse failed: {ex.Message}");
        }

        _logger.LogInformation("GenericRss fetched {Count} items from {Url} for source {SourceId}",
            results.Count, parameters.FeedUrl, source.Id);

        return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(results);
    }

    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";

    private static IngestedOpportunity? MapItem(XElement item, GenericRssParameters parameters)
    {
        var title = item.Element("title")?.Value
            ?? item.Element(AtomNs + "title")?.Value;
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var guid = item.Element("guid")?.Value
            ?? item.Element(AtomNs + "id")?.Value
            ?? item.Element("link")?.Value
            ?? item.Element(AtomNs + "link")?.Attribute("href")?.Value
            ?? title;

        var description = item.Element("description")?.Value
            ?? item.Element(AtomNs + "summary")?.Value
            ?? item.Element(AtomNs + "content")?.Value
            ?? string.Empty;

        var pubDateRaw = item.Element("pubDate")?.Value
            ?? item.Element(AtomNs + "published")?.Value
            ?? item.Element(AtomNs + "updated")?.Value;
        var postedDate = TryParseDate(pubDateRaw) ?? DateTimeOffset.UtcNow;

        return new IngestedOpportunity(
            ExternalId: guid,
            Title: title.Trim(),
            Description: description.Trim(),
            AgencyName: parameters.AgencyName,
            AgencyType: parameters.IsDefense
                ? AgencyType.FederalDefense
                : parameters.AgencyState is not null ? AgencyType.StateLocal : AgencyType.FederalCivilian,
            AgencyState: parameters.AgencyState,
            PostedDate: postedDate,
            ResponseDeadline: null,
            NaicsCode: null,
            EstimatedValue: null,
            ProcurementVehicle: null);
    }

    private static bool MatchesFilters(IngestedOpportunity opp, GenericRssParameters parameters)
    {
        var haystack = $"{opp.Title} {opp.Description}".ToLowerInvariant();

        if (parameters.IncludeKeywords is { Count: > 0 })
        {
            var anyMatch = parameters.IncludeKeywords.Any(k => haystack.Contains(k.ToLowerInvariant()));
            if (!anyMatch) return false;
        }

        if (parameters.ExcludeKeywords is { Count: > 0 })
        {
            var anyMatch = parameters.ExcludeKeywords.Any(k => haystack.Contains(k.ToLowerInvariant()));
            if (anyMatch) return false;
        }

        return true;
    }

    private static DateTimeOffset? TryParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var result))
            return result;
        return null;
    }
}
