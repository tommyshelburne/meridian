using System.Net.Http.Headers;
using System.Net.Http.Json;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Crm.HubSpot;

// HubSpot CRM v3 adapter. Auth is `Authorization: Bearer {access_token}` —
// works for both OAuth access tokens and Private App access tokens, which is
// why this adapter ships before the OAuth broker. Org/Deal/Activity wrap
// everything in HubSpot's `{ properties: {...}, associations: [...] }`
// envelope.
public class HubSpotAdapter : ICrmAdapter
{
    private readonly HttpClient _httpClient;
    private readonly HubSpotOptions _options;
    private readonly ILogger<HubSpotAdapter> _logger;

    public CrmProvider Provider => CrmProvider.HubSpot;

    public HubSpotAdapter(HttpClient httpClient, IOptions<HubSpotOptions> options, ILogger<HubSpotAdapter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<string>> FindOrCreateOrganizationAsync(
        CrmConnectionContext ctx, string agencyName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agencyName))
            return ServiceResult<string>.Fail("Organization name is required.");

        var search = await SendAsync<HubSpotSearchResponse>(ctx, HttpMethod.Post,
            "crm/v3/objects/companies/search",
            new
            {
                filterGroups = new[]
                {
                    new
                    {
                        filters = new[]
                        {
                            new { propertyName = "name", @operator = "EQ", value = agencyName }
                        }
                    }
                },
                properties = new[] { "name" },
                limit = 1
            }, ct);
        if (!search.IsSuccess) return ServiceResult<string>.Fail(search.Error!);

        var match = search.Value!.Results.FirstOrDefault();
        if (match is not null)
            return ServiceResult<string>.Ok(match.Id);

        var create = await SendAsync<HubSpotObject>(ctx, HttpMethod.Post,
            "crm/v3/objects/companies",
            new { properties = new Dictionary<string, string> { ["name"] = agencyName } }, ct);
        return create.IsSuccess
            ? ServiceResult<string>.Ok(create.Value!.Id)
            : ServiceResult<string>.Fail(create.Error!);
    }

    public async Task<ServiceResult<string>> CreateDealAsync(
        CrmConnectionContext ctx, Opportunity opportunity, string organizationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            return ServiceResult<string>.Fail("HubSpot company id is required.");

        var properties = new Dictionary<string, string>
        {
            ["dealname"] = opportunity.Title
        };
        if (opportunity.EstimatedValue.HasValue)
            properties["amount"] = opportunity.EstimatedValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(ctx.DefaultPipelineId))
            properties["pipeline"] = ctx.DefaultPipelineId;

        var body = new
        {
            properties,
            associations = new[]
            {
                new
                {
                    to = new { id = organizationId },
                    types = new[]
                    {
                        // 5 = deal-to-company, the HubSpot canonical association type id.
                        new { associationCategory = "HUBSPOT_DEFINED", associationTypeId = 5 }
                    }
                }
            }
        };

        var create = await SendAsync<HubSpotObject>(ctx, HttpMethod.Post, "crm/v3/objects/deals", body, ct);
        return create.IsSuccess
            ? ServiceResult<string>.Ok(create.Value!.Id)
            : ServiceResult<string>.Fail(create.Error!);
    }

    public async Task<ServiceResult> UpdateDealStageAsync(
        CrmConnectionContext ctx, string dealId, string stage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dealId))
            return ServiceResult.Fail("HubSpot deal id is required.");
        if (string.IsNullOrWhiteSpace(stage))
            return ServiceResult.Fail("HubSpot stage id is required.");

        var update = await SendAsync<HubSpotObject>(ctx, HttpMethod.Patch,
            $"crm/v3/objects/deals/{Uri.EscapeDataString(dealId)}",
            new { properties = new Dictionary<string, string> { ["dealstage"] = stage } }, ct);
        return update.IsSuccess ? ServiceResult.Ok() : ServiceResult.Fail(update.Error!);
    }

    public async Task<ServiceResult> AddActivityAsync(
        CrmConnectionContext ctx, string dealId, string type, string description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dealId))
            return ServiceResult.Fail("HubSpot deal id is required.");

        // HubSpot's activity model splits across object types (notes, tasks,
        // calls, emails, meetings). Default to notes — the safest catch-all
        // for sequence-engine breadcrumbs. Maps "task" / "call" through if the
        // caller specifies them.
        var (path, properties, associationTypeId) = type?.ToLowerInvariant() switch
        {
            "task"     => ("crm/v3/objects/tasks",
                           new Dictionary<string, string>
                           {
                               ["hs_task_subject"] = description,
                               ["hs_task_status"] = "NOT_STARTED",
                               ["hs_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                           },
                           // 216 = task-to-deal
                           216),
            "call"     => ("crm/v3/objects/calls",
                           new Dictionary<string, string>
                           {
                               ["hs_call_body"] = description,
                               ["hs_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                           },
                           // 206 = call-to-deal
                           206),
            _          => ("crm/v3/objects/notes",
                           new Dictionary<string, string>
                           {
                               ["hs_note_body"] = description,
                               ["hs_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                           },
                           // 214 = note-to-deal
                           214)
        };

        var body = new
        {
            properties,
            associations = new[]
            {
                new
                {
                    to = new { id = dealId },
                    types = new[]
                    {
                        new { associationCategory = "HUBSPOT_DEFINED", associationTypeId }
                    }
                }
            }
        };

        var create = await SendAsync<HubSpotObject>(ctx, HttpMethod.Post, path, body, ct);
        return create.IsSuccess ? ServiceResult.Ok() : ServiceResult.Fail(create.Error!);
    }

    private Uri BuildUri(CrmConnectionContext ctx, string relativeUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(ctx.ApiBaseUrl) ? _options.BaseUrl : ctx.ApiBaseUrl;
        if (!baseUrl.EndsWith('/')) baseUrl += '/';
        return new Uri(new Uri(baseUrl), relativeUrl);
    }

    private async Task<ServiceResult<T>> SendAsync<T>(
        CrmConnectionContext ctx, HttpMethod method, string relativeUrl, object? body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.AuthToken))
            return ServiceResult<T>.Fail("HubSpot access token is required.");

        var uri = BuildUri(ctx, relativeUrl);
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AuthToken);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await ReadErrorAsync(response, ct);
                return ServiceResult<T>.Fail(
                    $"HubSpot {(int)response.StatusCode}: {errorText}");
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(ct);
            return payload is null
                ? ServiceResult<T>.Fail("HubSpot returned an empty body.")
                : ServiceResult<T>.Ok(payload);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HubSpot {Method} {Url} failed", method.Method, uri);
            return ServiceResult<T>.Fail($"HubSpot request failed: {ex.Message}");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<HubSpotErrorEnvelope>(ct);
            if (!string.IsNullOrEmpty(error?.Message))
                return error.Message!;
        }
        catch
        {
            // Fall back to raw body below.
        }
        var raw = await response.Content.ReadAsStringAsync(ct);
        return raw.Length > 500 ? raw[..500] + "…" : raw;
    }
}
