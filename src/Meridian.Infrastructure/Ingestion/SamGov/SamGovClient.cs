using System.Globalization;
using System.Net.Http.Json;
using System.Web;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Ingestion.SamGov;

public class SamGovClient : IOpportunitySource
{
    private readonly HttpClient _httpClient;
    private readonly SamGovOptions _options;
    private readonly ILogger<SamGovClient> _logger;

    public string SourceName => "SAM.gov";

    public SamGovClient(HttpClient httpClient, IOptions<SamGovOptions> options, ILogger<SamGovClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<Opportunity>>> FetchOpportunitiesAsync(
        Guid tenantId, CancellationToken ct)
    {
        var allOpportunities = new List<Opportunity>();
        var seenIds = new HashSet<string>();

        foreach (var keyword in _options.Keywords)
        {
            var keywordResults = await SearchByKeywordAsync(keyword, tenantId, ct);
            foreach (var opp in keywordResults)
            {
                if (seenIds.Add(opp.ExternalId))
                    allOpportunities.Add(opp);
            }
        }

        _logger.LogInformation("SAM.gov fetched {Count} unique opportunities across {Keywords} keywords",
            allOpportunities.Count, _options.Keywords.Count);

        return ServiceResult<IReadOnlyList<Opportunity>>.Ok(allOpportunities);
    }

    private async Task<List<Opportunity>> SearchByKeywordAsync(string keyword, Guid tenantId, CancellationToken ct)
    {
        var results = new List<Opportunity>();
        var postedFrom = DateTimeOffset.UtcNow.AddDays(-_options.LookbackDays).ToString("MM/dd/yyyy");
        var postedTo = DateTimeOffset.UtcNow.ToString("MM/dd/yyyy");

        for (var page = 0; page < _options.MaxPages; page++)
        {
            var offset = page * _options.PageSize;
            var url = BuildSearchUrl(keyword, postedFrom, postedTo, offset);

            try
            {
                var response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                var searchResult = await response.Content.ReadFromJsonAsync<SamGovSearchResponse>(ct);
                if (searchResult?.OpportunitiesData is null or { Count: 0 })
                    break;

                foreach (var samOpp in searchResult.OpportunitiesData)
                {
                    var opp = MapToOpportunity(samOpp, tenantId);
                    if (opp is not null)
                        results.Add(opp);
                }

                if (offset + _options.PageSize >= searchResult.TotalRecords)
                    break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SAM.gov search failed for keyword '{Keyword}' page {Page}", keyword, page);
                break;
            }
        }

        return results;
    }

    private string BuildSearchUrl(string keyword, string postedFrom, string postedTo, int offset)
    {
        var encodedKeyword = HttpUtility.UrlEncode(keyword);
        return $"{_options.BaseUrl}?api_key={_options.ApiKey}&q={encodedKeyword}" +
               $"&postedFrom={postedFrom}&postedTo={postedTo}" +
               $"&limit={_options.PageSize}&offset={offset}" +
               $"&ptype=o,p,k&status=active";
    }

    private static Opportunity? MapToOpportunity(SamGovOpportunity sam, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(sam.NoticeId) || string.IsNullOrWhiteSpace(sam.Title))
            return null;

        var agencyName = sam.SubTier ?? sam.Department ?? "Unknown Agency";
        var agencyType = ClassifyAgencyType(sam.Department);
        var agency = Agency.Create(agencyName, agencyType);

        DateTimeOffset? postedDate = TryParseDate(sam.PostedDate);
        DateTimeOffset? deadline = TryParseDate(sam.ResponseDeadline);

        var opp = Opportunity.Create(
            tenantId,
            sam.NoticeId,
            OpportunitySource.SamGov,
            sam.Title,
            sam.Description ?? string.Empty,
            agency,
            postedDate ?? DateTimeOffset.UtcNow,
            deadline,
            sam.NaicsCode,
            sam.Award?.Amount);

        return opp;
    }

    private static AgencyType ClassifyAgencyType(string? department)
    {
        if (string.IsNullOrWhiteSpace(department)) return AgencyType.FederalCivilian;

        var deptLower = department.ToLowerInvariant();
        if (deptLower.Contains("defense") || deptLower.Contains("army") ||
            deptLower.Contains("navy") || deptLower.Contains("air force"))
            return AgencyType.FederalDefense;

        return AgencyType.FederalCivilian;
    }

    internal static DateTimeOffset? TryParseDatePublic(string? dateStr) => TryParseDate(dateStr);

    private static DateTimeOffset? TryParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        var formats = new[] { "MMddyyyy", "MM/dd/yyyy", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd" };
        if (DateTimeOffset.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var result))
            return result;

        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out result))
            return result;

        return null;
    }
}
