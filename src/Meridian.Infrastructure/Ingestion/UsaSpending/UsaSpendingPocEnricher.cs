using System.Net.Http.Json;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Ingestion.UsaSpending;

public class UsaSpendingPocEnricher : IPocEnricher
{
    private readonly HttpClient _httpClient;
    private readonly UsaSpendingOptions _options;
    private readonly ILogger<UsaSpendingPocEnricher> _logger;

    public string SourceName => "USASpending POC";

    public UsaSpendingPocEnricher(
        HttpClient httpClient,
        IOptions<UsaSpendingOptions> options,
        ILogger<UsaSpendingPocEnricher> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<Contact>>> EnrichAsync(
        Opportunity opportunity, Guid tenantId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opportunity.NaicsCode))
            return ServiceResult<IReadOnlyList<Contact>>.Ok(Array.Empty<Contact>());

        var awardIds = await SearchAwardIdsAsync(opportunity, ct);
        if (awardIds.Count == 0)
            return ServiceResult<IReadOnlyList<Contact>>.Ok(Array.Empty<Contact>());

        var contacts = new List<Contact>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var awardId in awardIds)
        {
            var rep = await FetchPrimaryBusinessRepAsync(awardId, ct);
            if (rep is null || string.IsNullOrWhiteSpace(rep.Email)) continue;
            if (!seenEmails.Add(rep.Email!)) continue;

            contacts.Add(Contact.Create(
                tenantId,
                rep.Name ?? "Unknown",
                opportunity.Agency,
                ContactSource.UsaSpending,
                _options.PocEnricherBaseConfidence,
                title: rep.Title,
                email: rep.Email,
                phone: rep.Phone));
        }

        return ServiceResult<IReadOnlyList<Contact>>.Ok(contacts);
    }

    private async Task<IReadOnlyList<string>> SearchAwardIdsAsync(Opportunity opportunity, CancellationToken ct)
    {
        var startDate = DateTimeOffset.UtcNow.AddDays(-_options.PocEnricherLookbackDays).ToString("yyyy-MM-dd");
        var endDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

        var request = new UsaSpendingSearchRequest
        {
            Page = 1,
            Limit = _options.PocEnricherMaxAwards,
            Filters = new UsaSpendingFilters
            {
                TimePeriod = new List<UsaSpendingTimePeriod>
                {
                    new() { StartDate = startDate, EndDate = endDate }
                },
                NaicsCodes = new List<string> { opportunity.NaicsCode! },
                Keywords = new List<string> { opportunity.Agency.Name }
            }
        };

        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync(_options.BaseUrl, request, ct);
            httpResponse.EnsureSuccessStatusCode();
            var response = await httpResponse.Content.ReadFromJsonAsync<UsaSpendingSearchResponse>(cancellationToken: ct);

            return response?.Results?
                .Where(r => !string.IsNullOrWhiteSpace(r.AwardId))
                .Select(r => r.AwardId!)
                .Take(_options.PocEnricherMaxAwards)
                .ToList()
                ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USASpending POC search failed for opportunity {OpportunityId}", opportunity.Id);
            return Array.Empty<string>();
        }
    }

    private async Task<UsaSpendingBusinessRepresentative?> FetchPrimaryBusinessRepAsync(
        string awardId, CancellationToken ct)
    {
        var url = $"{_options.AwardDetailBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(awardId)}/";

        try
        {
            var httpResponse = await _httpClient.GetAsync(url, ct);
            httpResponse.EnsureSuccessStatusCode();
            var detail = await httpResponse.Content.ReadFromJsonAsync<UsaSpendingAwardDetail>(cancellationToken: ct);
            return detail?.Recipient?.PrimaryBusinessRepresentative;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USASpending award detail fetch failed for {AwardId}", awardId);
            return null;
        }
    }
}
