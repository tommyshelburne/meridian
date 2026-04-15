using System.Net.Http.Json;
using System.Web;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Ingestion.SamGov;

public class SamGovPocEnricher : IPocEnricher
{
    private readonly HttpClient _httpClient;
    private readonly SamGovOptions _options;
    private readonly ILogger<SamGovPocEnricher> _logger;

    public string SourceName => "SAM.gov POC";

    public SamGovPocEnricher(HttpClient httpClient, IOptions<SamGovOptions> options, ILogger<SamGovPocEnricher> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<Contact>>> EnrichAsync(
        Opportunity opportunity, Guid tenantId, CancellationToken ct)
    {
        var contacts = new List<Contact>();

        // Strategy 1: Check POC data embedded in the opportunity's own listing
        var oppContacts = await FetchOpportunityPocAsync(opportunity.ExternalId, ct);
        contacts.AddRange(oppContacts.Select(poc => MapToContact(poc, opportunity, tenantId, 0.90f)));

        // Strategy 2: Search for award contacts at same agency + NAICS
        if (contacts.Count == 0 && opportunity.NaicsCode is not null)
        {
            var awardContacts = await SearchAwardContactsAsync(
                opportunity.Agency.Name, opportunity.NaicsCode, ct);
            contacts.AddRange(awardContacts.Select(poc => MapToContact(poc, opportunity, tenantId, 0.70f)));
        }

        return ServiceResult<IReadOnlyList<Contact>>.Ok(
            contacts.Where(c => c.Email is not null).ToList());
    }

    private async Task<List<SamGovPointOfContact>> FetchOpportunityPocAsync(string noticeId, CancellationToken ct)
    {
        try
        {
            var url = $"{_options.BaseUrl}?api_key={_options.ApiKey}" +
                      $"&noticeid={HttpUtility.UrlEncode(noticeId)}&limit=1";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SamGovSearchResponse>(ct);
            return result?.OpportunitiesData?.FirstOrDefault()?.PointOfContact ?? new List<SamGovPointOfContact>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch POC for notice {NoticeId}", noticeId);
            return new List<SamGovPointOfContact>();
        }
    }

    private async Task<List<SamGovPointOfContact>> SearchAwardContactsAsync(
        string agencyName, string naicsCode, CancellationToken ct)
    {
        try
        {
            var url = $"{_options.BaseUrl}?api_key={_options.ApiKey}" +
                      $"&q={HttpUtility.UrlEncode(agencyName)}&ncode={naicsCode}" +
                      $"&ptype=a&limit=5";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SamGovSearchResponse>(ct);
            return result?.OpportunitiesData?
                .SelectMany(o => o.PointOfContact ?? Enumerable.Empty<SamGovPointOfContact>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Email))
                .DistinctBy(p => p.Email?.ToLowerInvariant())
                .Take(3)
                .ToList() ?? new List<SamGovPointOfContact>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search award contacts for {Agency}", agencyName);
            return new List<SamGovPointOfContact>();
        }
    }

    private static Contact MapToContact(SamGovPointOfContact poc, Opportunity opportunity,
        Guid tenantId, float baseConfidence)
    {
        return Contact.Create(
            tenantId,
            poc.FullName ?? "Unknown",
            opportunity.Agency,
            ContactSource.SamGov,
            baseConfidence,
            title: poc.Title,
            email: poc.Email,
            phone: poc.Phone);
    }
}
