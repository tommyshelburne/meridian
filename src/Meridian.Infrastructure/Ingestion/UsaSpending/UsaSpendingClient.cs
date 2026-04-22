using System.Globalization;
using System.Net.Http.Json;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Ingestion.UsaSpending;

public class UsaSpendingClient : IOpportunitySourceAdapter
{
    private readonly HttpClient _httpClient;
    private readonly UsaSpendingOptions _options;
    private readonly ILogger<UsaSpendingClient> _logger;

    public SourceAdapterType AdapterType => SourceAdapterType.UsaSpending;

    public UsaSpendingClient(
        HttpClient httpClient,
        IOptions<UsaSpendingOptions> options,
        ILogger<UsaSpendingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
        SourceDefinition source, CancellationToken ct)
    {
        var parameters = UsaSpendingParameters.Parse(source.ParametersJson, _options);
        var results = new List<IngestedOpportunity>();
        var seen = new HashSet<string>();

        var startDate = DateTimeOffset.UtcNow.AddDays(-parameters.LookbackDays).ToString("yyyy-MM-dd");
        var endDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

        for (var page = 1; page <= _options.MaxPages; page++)
        {
            var request = BuildRequest(parameters, startDate, endDate, page);

            UsaSpendingSearchResponse? response;
            try
            {
                var httpResponse = await _httpClient.PostAsJsonAsync(_options.BaseUrl, request, ct);
                httpResponse.EnsureSuccessStatusCode();
                response = await httpResponse.Content.ReadFromJsonAsync<UsaSpendingSearchResponse>(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "USASpending search failed on page {Page}", page);
                break;
            }

            if (response?.Results is null or { Count: 0 })
                break;

            foreach (var award in response.Results)
            {
                var ingested = MapToIngested(award);
                if (ingested is not null && seen.Add(ingested.ExternalId))
                    results.Add(ingested);
            }

            if (response.PageMetadata is null || !response.PageMetadata.HasNext)
                break;
        }

        _logger.LogInformation("USASpending fetched {Count} awards for source {SourceId}",
            results.Count, source.Id);

        return ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(results);
    }

    private UsaSpendingSearchRequest BuildRequest(
        UsaSpendingParameters parameters, string startDate, string endDate, int page)
    {
        var request = new UsaSpendingSearchRequest
        {
            Page = page,
            Limit = _options.PageSize,
            Filters = new UsaSpendingFilters
            {
                TimePeriod = new List<UsaSpendingTimePeriod>
                {
                    new() { StartDate = startDate, EndDate = endDate }
                }
            }
        };

        if (parameters.NaicsCodes.Count > 0)
            request.Filters.NaicsCodes = parameters.NaicsCodes.ToList();

        if (parameters.Keywords is { Count: > 0 })
            request.Filters.Keywords = parameters.Keywords.ToList();

        if (parameters.MinAwardAmount.HasValue)
            request.Filters.AwardAmounts = new List<UsaSpendingAmountRange>
            {
                new() { LowerBound = parameters.MinAwardAmount.Value }
            };

        return request;
    }

    private static IngestedOpportunity? MapToIngested(UsaSpendingResult award)
    {
        if (string.IsNullOrWhiteSpace(award.AwardId))
            return null;

        var title = !string.IsNullOrWhiteSpace(award.Description)
            ? award.Description
            : $"Award {award.AwardId}";

        var agencyName = award.AwardingSubAgency ?? award.AwardingAgency ?? "Unknown Agency";
        var agencyType = ClassifyAgencyType(award.AwardingAgency);

        var postedDate = TryParseDate(award.LastModifiedDate)
            ?? TryParseDate(award.StartDate)
            ?? DateTimeOffset.UtcNow;
        var endDate = TryParseDate(award.EndDate);

        return new IngestedOpportunity(
            ExternalId: award.AwardId,
            Title: title,
            Description: award.Description ?? string.Empty,
            AgencyName: agencyName,
            AgencyType: agencyType,
            AgencyState: null,
            PostedDate: postedDate,
            ResponseDeadline: endDate,
            NaicsCode: award.NaicsCode,
            EstimatedValue: award.AwardAmount,
            ProcurementVehicle: null,
            Metadata: BuildMetadata(award));
    }

    private static Dictionary<string, string> BuildMetadata(UsaSpendingResult award)
    {
        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(award.RecipientName))
            metadata["recipient"] = award.RecipientName;
        if (!string.IsNullOrWhiteSpace(award.StartDate))
            metadata["awardStartDate"] = award.StartDate;
        if (!string.IsNullOrWhiteSpace(award.EndDate))
            metadata["awardEndDate"] = award.EndDate;
        return metadata;
    }

    private static AgencyType ClassifyAgencyType(string? agency)
    {
        if (string.IsNullOrWhiteSpace(agency)) return AgencyType.FederalCivilian;

        var lower = agency.ToLowerInvariant();
        if (lower.Contains("defense") || lower.Contains("army") ||
            lower.Contains("navy") || lower.Contains("air force"))
            return AgencyType.FederalDefense;

        return AgencyType.FederalCivilian;
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
