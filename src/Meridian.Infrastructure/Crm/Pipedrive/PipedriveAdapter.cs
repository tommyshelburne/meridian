using System.Net.Http.Json;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Crm.Pipedrive;

// Pipedrive REST adapter. v1.0 uses personal-API-token auth (token passed as
// the `api_token` query param). OAuth token refresh and webhook intake are
// scheduled for the OAuth slice — until then `ctx.RefreshToken`/`ExpiresAt`
// are unused, and a 401 from Pipedrive surfaces as a failed ServiceResult.
public class PipedriveAdapter : ICrmAdapter
{
    private readonly HttpClient _httpClient;
    private readonly PipedriveOptions _options;
    private readonly ILogger<PipedriveAdapter> _logger;

    public CrmProvider Provider => CrmProvider.Pipedrive;

    public PipedriveAdapter(HttpClient httpClient, IOptions<PipedriveOptions> options, ILogger<PipedriveAdapter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<ServiceResult<string>> FindOrCreateOrganizationAsync(
        CrmConnectionContext ctx, string agencyName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agencyName))
            return ServiceResult<string>.Fail("Organization name is required.");

        var search = await GetEnvelopeAsync<PipedriveSearchResult>(
            $"organizations/search?term={Uri.EscapeDataString(agencyName)}&exact_match=true&{TokenQuery(ctx)}", ct);
        if (!search.IsSuccess) return ServiceResult<string>.Fail(search.Error!);

        var existing = search.Value!.Items.FirstOrDefault(i =>
            string.Equals(i.Item.Name, agencyName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return ServiceResult<string>.Ok(existing.Item.Id.ToString());

        var create = await PostEnvelopeAsync<PipedriveOrganization>(
            $"organizations?{TokenQuery(ctx)}", new { name = agencyName }, ct);
        return create.IsSuccess
            ? ServiceResult<string>.Ok(create.Value!.Id.ToString())
            : ServiceResult<string>.Fail(create.Error!);
    }

    public async Task<ServiceResult<string>> CreateDealAsync(
        CrmConnectionContext ctx, Opportunity opportunity, string organizationId, CancellationToken ct)
    {
        if (!int.TryParse(organizationId, out var orgId))
            return ServiceResult<string>.Fail($"Pipedrive organization id must be numeric; got '{organizationId}'.");

        var body = new Dictionary<string, object?>
        {
            ["title"] = opportunity.Title,
            ["org_id"] = orgId
        };
        if (opportunity.EstimatedValue.HasValue)
        {
            body["value"] = opportunity.EstimatedValue.Value;
            body["currency"] = "USD";
        }
        if (!string.IsNullOrWhiteSpace(ctx.DefaultPipelineId) &&
            int.TryParse(ctx.DefaultPipelineId, out var pipelineId))
        {
            body["pipeline_id"] = pipelineId;
        }

        var create = await PostEnvelopeAsync<PipedriveDeal>($"deals?{TokenQuery(ctx)}", body, ct);
        return create.IsSuccess
            ? ServiceResult<string>.Ok(create.Value!.Id.ToString())
            : ServiceResult<string>.Fail(create.Error!);
    }

    public async Task<ServiceResult> UpdateDealStageAsync(
        CrmConnectionContext ctx, string dealId, string stage, CancellationToken ct)
    {
        if (!int.TryParse(dealId, out var id))
            return ServiceResult.Fail($"Pipedrive deal id must be numeric; got '{dealId}'.");
        if (!int.TryParse(stage, out var stageId))
            return ServiceResult.Fail($"Pipedrive stage id must be numeric; got '{stage}'.");

        var update = await PutEnvelopeAsync<PipedriveDeal>(
            $"deals/{id}?{TokenQuery(ctx)}", new { stage_id = stageId }, ct);
        return update.IsSuccess ? ServiceResult.Ok() : ServiceResult.Fail(update.Error!);
    }

    public async Task<ServiceResult> AddActivityAsync(
        CrmConnectionContext ctx, string dealId, string type, string description, CancellationToken ct)
    {
        if (!int.TryParse(dealId, out var id))
            return ServiceResult.Fail($"Pipedrive deal id must be numeric; got '{dealId}'.");

        var body = new Dictionary<string, object?>
        {
            ["deal_id"] = id,
            ["type"] = string.IsNullOrWhiteSpace(type) ? "task" : type,
            ["subject"] = description
        };

        var create = await PostEnvelopeAsync<PipedriveActivity>($"activities?{TokenQuery(ctx)}", body, ct);
        return create.IsSuccess ? ServiceResult.Ok() : ServiceResult.Fail(create.Error!);
    }

    private static string TokenQuery(CrmConnectionContext ctx) =>
        $"api_token={Uri.EscapeDataString(ctx.AuthToken)}";

    private async Task<ServiceResult<T>> GetEnvelopeAsync<T>(string relativeUrl, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(relativeUrl, ct);
            return await ReadEnvelopeAsync<T>(response, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Pipedrive GET {Url} failed", relativeUrl);
            return ServiceResult<T>.Fail($"Pipedrive request failed: {ex.Message}");
        }
    }

    private async Task<ServiceResult<T>> PostEnvelopeAsync<T>(string relativeUrl, object body, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(relativeUrl, body, ct);
            return await ReadEnvelopeAsync<T>(response, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Pipedrive POST {Url} failed", relativeUrl);
            return ServiceResult<T>.Fail($"Pipedrive request failed: {ex.Message}");
        }
    }

    private async Task<ServiceResult<T>> PutEnvelopeAsync<T>(string relativeUrl, object body, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(relativeUrl, body, ct);
            return await ReadEnvelopeAsync<T>(response, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Pipedrive PUT {Url} failed", relativeUrl);
            return ServiceResult<T>.Fail($"Pipedrive request failed: {ex.Message}");
        }
    }

    private static async Task<ServiceResult<T>> ReadEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return ServiceResult<T>.Fail($"Pipedrive {(int)response.StatusCode}: {Truncate(body, 500)}");
        }

        var envelope = await response.Content.ReadFromJsonAsync<PipedriveEnvelope<T>>(ct);
        if (envelope is null)
            return ServiceResult<T>.Fail("Pipedrive returned an empty body.");
        if (!envelope.Success || envelope.Data is null)
            return ServiceResult<T>.Fail(envelope.Error ?? "Pipedrive reported failure with no error message.");
        return ServiceResult<T>.Ok(envelope.Data);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
