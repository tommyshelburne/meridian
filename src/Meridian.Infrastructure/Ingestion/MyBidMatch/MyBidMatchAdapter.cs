using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Ingestion.MyBidMatch;

public class MyBidMatchAdapter : IOpportunitySourceAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MyBidMatchAdapter> _logger;

    public SourceAdapterType AdapterType => SourceAdapterType.StatePortal;

    public MyBidMatchAdapter(HttpClient httpClient, ILogger<MyBidMatchAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
        SourceDefinition source, CancellationToken ct)
    {
        var parameters = MyBidMatchParameters.Parse(source.ParametersJson);
        if (parameters is null)
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                "MyBidMatch parameters require 'subscriptionId'.");

        var indexHtml = await GetHtmlAsync(
            $"{parameters.BaseUrl}/go?sub={parameters.SubscriptionId}", ct);
        if (indexHtml is null)
            return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(
                "MyBidMatch index fetch failed.");

        var docGroupIds = MyBidMatchParser.ParseDocGroupIds(indexHtml);
        var results = new List<IngestedOpportunity>();

        foreach (var docId in docGroupIds)
        {
            if (ct.IsCancellationRequested) break;

            var docHtml = await GetHtmlAsync($"{parameters.BaseUrl}/go?doc={docId}", ct);
            if (docHtml is null) continue;

            var articleIds = MyBidMatchParser.ParseArticleIds(docHtml, docId);

            foreach (var articleId in articleIds)
            {
                if (ct.IsCancellationRequested) break;

                var seq = articleId.Split(':')[1];
                var articleHtml = await GetHtmlAsync(
                    $"{parameters.BaseUrl}/article?doc={docId}&seq={seq}", ct);
                if (articleHtml is null) continue;

                var parsed = MyBidMatchParser.ParseArticle(articleHtml, articleId);
                if (string.IsNullOrWhiteSpace(parsed.Title)) continue;

                results.Add(new IngestedOpportunity(
                    ExternalId: parsed.ExternalId,
                    Title: parsed.Title,
                    Description: parsed.Body,
                    AgencyName: string.IsNullOrWhiteSpace(parsed.Agency) ? "Unknown Agency" : parsed.Agency,
                    AgencyType: AgencyType.StateLocal,
                    AgencyState: parameters.AgencyState,
                    PostedDate: DateTimeOffset.UtcNow,
                    ResponseDeadline: null,
                    NaicsCode: null,
                    EstimatedValue: null,
                    ProcurementVehicle: null));
            }
        }

        _logger.LogInformation(
            "MyBidMatch fetched {Count} articles from {DocGroups} doc groups for source {SourceId}",
            results.Count, docGroupIds.Count, source.Id);

        return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(results);
    }

    private async Task<string?> GetHtmlAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "MyBidMatch request failed for {Url}", url);
            return null;
        }
    }
}
