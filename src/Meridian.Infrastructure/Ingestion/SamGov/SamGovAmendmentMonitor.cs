using System.Net.Http.Json;
using System.Web;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Ingestion.SamGov;

public class SamGovAmendmentMonitor : IBidMonitor
{
    private readonly HttpClient _httpClient;
    private readonly SamGovOptions _options;
    private readonly ILogger<SamGovAmendmentMonitor> _logger;

    public SamGovAmendmentMonitor(HttpClient httpClient, IOptions<SamGovOptions> options,
        ILogger<SamGovAmendmentMonitor> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<AmendmentUpdate>>> CheckForUpdatesAsync(
        IReadOnlyList<Opportunity> watchedOpportunities, CancellationToken ct)
    {
        var updates = new List<AmendmentUpdate>();

        foreach (var opp in watchedOpportunities.Where(o => o.Source == Domain.Common.OpportunitySource.SamGov))
        {
            try
            {
                var url = $"{_options.BaseUrl}?api_key={_options.ApiKey}" +
                          $"&noticeid={HttpUtility.UrlEncode(opp.ExternalId)}&limit=1";
                var response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<SamGovSearchResponse>(ct);
                var current = result?.OpportunitiesData?.FirstOrDefault();
                if (current is null) continue;

                var currentDeadline = SamGovClient.TryParseDatePublic(current.ResponseDeadline);

                // Detect changes
                var hasNewDeadline = currentDeadline.HasValue && currentDeadline != opp.ResponseDeadline;
                var hasNewTitle = current.Title != opp.Title;

                if (hasNewDeadline || hasNewTitle)
                {
                    updates.Add(new AmendmentUpdate(
                        opp.ExternalId,
                        DateTimeOffset.UtcNow,
                        hasNewTitle ? current.Title : null,
                        hasNewDeadline ? currentDeadline : null));

                    _logger.LogInformation("Amendment detected for {NoticeId}: deadline={Deadline}, title changed={TitleChanged}",
                        opp.ExternalId, hasNewDeadline, hasNewTitle);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check amendment for {NoticeId}", opp.ExternalId);
            }
        }

        return ServiceResult<IReadOnlyList<AmendmentUpdate>>.Ok(updates);
    }
}
