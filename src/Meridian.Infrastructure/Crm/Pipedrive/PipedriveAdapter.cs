using System.Net.Http.Json;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Crm.Pipedrive;

// Pipedrive REST adapter. Auth is the access token from the connection
// context, passed as the `api_token` query param — Pipedrive accepts both
// personal API tokens and OAuth access tokens through this parameter.
// Per-tenant api_domain (set during OAuth provisioning) is honored via
// ctx.ApiBaseUrl; personal-token connections fall back to the static base.
// Token refresh on expiry is handled upstream by CrmConnectionService.
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
        // Don't fix BaseAddress here — each request resolves against the
        // tenant-specific api_domain (CrmConnectionContext.ApiBaseUrl) when
        // the connection was provisioned via OAuth, otherwise falls back to
        // the configured default. A static BaseAddress would force every
        // tenant onto the same Pipedrive domain regardless of OAuth scope.
    }

    public async Task<ServiceResult<string>> FindOrCreateOrganizationAsync(
        CrmConnectionContext ctx, string agencyName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agencyName))
            return ServiceResult<string>.Fail("Organization name is required.");

        var search = await GetEnvelopeAsync<PipedriveSearchResult>(
            ctx,
            $"organizations/search?term={Uri.EscapeDataString(agencyName)}&exact_match=true&{TokenQuery(ctx)}", ct);
        if (!search.IsSuccess) return ServiceResult<string>.Fail(search.Error!);

        var existing = search.Value!.Items.FirstOrDefault(i =>
            string.Equals(i.Item.Name, agencyName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return ServiceResult<string>.Ok(existing.Item.Id.ToString());

        var create = await PostEnvelopeAsync<PipedriveOrganization>(
            ctx, $"organizations?{TokenQuery(ctx)}", new { name = agencyName }, ct);
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

        var create = await PostEnvelopeAsync<PipedriveDeal>(ctx, $"deals?{TokenQuery(ctx)}", body, ct);
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
            ctx, $"deals/{id}?{TokenQuery(ctx)}", new { stage_id = stageId }, ct);
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

        var create = await PostEnvelopeAsync<PipedriveActivity>(ctx, $"activities?{TokenQuery(ctx)}", body, ct);
        return create.IsSuccess ? ServiceResult.Ok() : ServiceResult.Fail(create.Error!);
    }

    private static string TokenQuery(CrmConnectionContext ctx) =>
        $"api_token={Uri.EscapeDataString(ctx.AuthToken)}";

    private Uri BuildUri(CrmConnectionContext ctx, string relativeUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(ctx.ApiBaseUrl) ? _options.BaseUrl : ctx.ApiBaseUrl;
        if (!baseUrl.EndsWith('/')) baseUrl += '/';
        return new Uri(new Uri(baseUrl), relativeUrl);
    }

    private Task<ServiceResult<T>> GetEnvelopeAsync<T>(CrmConnectionContext ctx, string relativeUrl, CancellationToken ct)
        => SendEnvelopeAsync<T>(ctx, relativeUrl, "GET", body: null, ct);

    private Task<ServiceResult<T>> PostEnvelopeAsync<T>(CrmConnectionContext ctx, string relativeUrl, object body, CancellationToken ct)
        => SendEnvelopeAsync<T>(ctx, relativeUrl, "POST", body, ct);

    private Task<ServiceResult<T>> PutEnvelopeAsync<T>(CrmConnectionContext ctx, string relativeUrl, object body, CancellationToken ct)
        => SendEnvelopeAsync<T>(ctx, relativeUrl, "PUT", body, ct);

    private async Task<ServiceResult<T>> SendEnvelopeAsync<T>(
        CrmConnectionContext ctx, string relativeUrl, string method, object? body, CancellationToken ct)
    {
        var uri = BuildUri(ctx, relativeUrl);
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            using var response = await _httpClient.SendAsync(request, ct);
            return await ReadEnvelopeAsync<T>(response, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Pipedrive {Method} {Url} failed", method, uri);
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
