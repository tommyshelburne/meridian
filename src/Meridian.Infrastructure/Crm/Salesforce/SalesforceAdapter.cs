using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Crm.Salesforce;

// Salesforce REST adapter. Operates against the per-tenant `instance_url`
// (carried as ctx.ApiBaseUrl) under `/services/data/{version}/`. Authentication
// is OAuth-only (no personal token equivalent), so this adapter ships paired
// with SalesforceOAuthBroker — without an active OAuth connection the
// pipeline routes to the Noop adapter via the empty-context fallback.
//
// Salesforce-specific quirks the port surface has to absorb:
//  - Opportunity.CloseDate is required at create time (no Salesforce default).
//  - Opportunity.StageName is required at create time. Defaults to a
//    configurable starter stage; tenants override via the future
//    stage-mapping table.
//  - Account search uses SOQL via the /query endpoint, not a dedicated
//    /search endpoint.
public class SalesforceAdapter : ICrmAdapter
{
    // Salesforce REST sobject fields are PascalCase (Name, AccountId, StageName,
    // ...). JsonContent.Create's default uses JsonSerializerDefaults.Web which
    // camelCases — so we explicitly send body JSON with the property names left
    // as written. Read-side options stay default since the response DTOs declare
    // [JsonPropertyName] attributes.
    private static readonly JsonSerializerOptions BodyOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly HttpClient _httpClient;
    private readonly SalesforceOptions _options;
    private readonly ILogger<SalesforceAdapter> _logger;

    public CrmProvider Provider => CrmProvider.Salesforce;

    public SalesforceAdapter(HttpClient httpClient, IOptions<SalesforceOptions> options, ILogger<SalesforceAdapter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<string>> FindOrCreateOrganizationAsync(
        CrmConnectionContext ctx, string agencyName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agencyName))
            return ServiceResult<string>.Fail("Account name is required.");

        var soql = $"SELECT Id FROM Account WHERE Name = '{EscapeSoql(agencyName)}' LIMIT 1";
        var search = await SendAsync<SalesforceQueryResponse>(ctx, HttpMethod.Get,
            $"query?q={Uri.EscapeDataString(soql)}", body: null, ct);
        if (!search.IsSuccess) return ServiceResult<string>.Fail(search.Error!);

        var match = search.Value!.Records.FirstOrDefault();
        if (match is not null)
            return ServiceResult<string>.Ok(match.Id);

        var create = await SendAsync<SalesforceCreateResponse>(ctx, HttpMethod.Post,
            "sobjects/Account/", new { Name = agencyName }, ct);
        return UnwrapCreate(create, "account");
    }

    public async Task<ServiceResult<string>> CreateDealAsync(
        CrmConnectionContext ctx, Opportunity opportunity, string organizationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            return ServiceResult<string>.Fail("Salesforce account id is required.");

        var closeDate = (opportunity.ResponseDeadline
            ?? DateTimeOffset.UtcNow.AddDays(_options.DefaultCloseDateDays))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var body = new Dictionary<string, object?>
        {
            ["Name"] = opportunity.Title,
            ["AccountId"] = organizationId,
            ["StageName"] = _options.DefaultStageName,
            ["CloseDate"] = closeDate
        };
        if (opportunity.EstimatedValue.HasValue)
            body["Amount"] = opportunity.EstimatedValue.Value;

        var create = await SendAsync<SalesforceCreateResponse>(ctx, HttpMethod.Post,
            "sobjects/Opportunity/", body, ct);
        return UnwrapCreate(create, "opportunity");
    }

    public async Task<ServiceResult> UpdateDealStageAsync(
        CrmConnectionContext ctx, string dealId, string stage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dealId))
            return ServiceResult.Fail("Salesforce opportunity id is required.");
        if (string.IsNullOrWhiteSpace(stage))
            return ServiceResult.Fail("StageName is required.");

        // Salesforce returns 204 No Content on a successful PATCH. Treating
        // that as success without a JSON envelope.
        var update = await SendNoContentAsync(ctx, HttpMethod.Patch,
            $"sobjects/Opportunity/{Uri.EscapeDataString(dealId)}",
            new { StageName = stage }, ct);
        return update;
    }

    public async Task<ServiceResult> AddActivityAsync(
        CrmConnectionContext ctx, string dealId, string type, string description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dealId))
            return ServiceResult.Fail("Salesforce opportunity id is required.");

        // Activities live on the Task sobject. WhatId binds the Task to a
        // related entity (an Opportunity in this case). TaskSubtype maps the
        // adapter's free-form "type" into Salesforce's known set; anything
        // unrecognized falls through as a plain Task.
        var subtype = (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "call"  => "Call",
            "email" => "Email",
            "task"  => "Task",
            _       => "Task"
        };

        var body = new Dictionary<string, object?>
        {
            ["WhatId"] = dealId,
            ["Subject"] = Truncate(description, 255),
            ["Description"] = description,
            ["Status"] = "Completed",
            ["TaskSubtype"] = subtype
        };

        var create = await SendAsync<SalesforceCreateResponse>(ctx, HttpMethod.Post,
            "sobjects/Task/", body, ct);
        return create.IsSuccess && create.Value!.Success
            ? ServiceResult.Ok()
            : ServiceResult.Fail(create.IsSuccess
                ? FormatErrors(create.Value!.Errors) ?? "Salesforce reported failure."
                : create.Error!);
    }

    private Uri BuildUri(CrmConnectionContext ctx, string relativeUrl)
    {
        // ctx.ApiBaseUrl is set by SalesforceOAuthBroker to
        // {instance_url}/services/data/{version}/ at exchange-time. If somehow
        // missing (e.g. a test seeded a Salesforce connection by hand), fail
        // fast — there's no sane fallback host.
        if (string.IsNullOrWhiteSpace(ctx.ApiBaseUrl))
            throw new InvalidOperationException(
                "Salesforce connection is missing ApiBaseUrl; reconnect the tenant.");
        var baseUrl = ctx.ApiBaseUrl.EndsWith('/') ? ctx.ApiBaseUrl : ctx.ApiBaseUrl + "/";
        return new Uri(new Uri(baseUrl), relativeUrl);
    }

    private async Task<ServiceResult<T>> SendAsync<T>(
        CrmConnectionContext ctx, HttpMethod method, string relativeUrl, object? body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.AuthToken))
            return ServiceResult<T>.Fail("Salesforce access token is required.");

        Uri uri;
        try { uri = BuildUri(ctx, relativeUrl); }
        catch (InvalidOperationException ex) { return ServiceResult<T>.Fail(ex.Message); }

        try
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AuthToken);
            if (body is not null) request.Content = JsonContent.Create(body, options: BodyOptions);
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return ServiceResult<T>.Fail(await ReadErrorAsync(response, ct));

            var payload = await response.Content.ReadFromJsonAsync<T>(ct);
            return payload is null
                ? ServiceResult<T>.Fail("Salesforce returned an empty body.")
                : ServiceResult<T>.Ok(payload);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Salesforce {Method} {Url} failed", method.Method, uri);
            return ServiceResult<T>.Fail($"Salesforce request failed: {ex.Message}");
        }
    }

    private async Task<ServiceResult> SendNoContentAsync(
        CrmConnectionContext ctx, HttpMethod method, string relativeUrl, object? body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.AuthToken))
            return ServiceResult.Fail("Salesforce access token is required.");

        Uri uri;
        try { uri = BuildUri(ctx, relativeUrl); }
        catch (InvalidOperationException ex) { return ServiceResult.Fail(ex.Message); }

        try
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AuthToken);
            if (body is not null) request.Content = JsonContent.Create(body, options: BodyOptions);
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return ServiceResult.Fail(await ReadErrorAsync(response, ct));
            return ServiceResult.Ok();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Salesforce {Method} {Url} failed", method.Method, uri);
            return ServiceResult.Fail($"Salesforce request failed: {ex.Message}");
        }
    }

    private static ServiceResult<string> UnwrapCreate(ServiceResult<SalesforceCreateResponse> result, string label)
    {
        if (!result.IsSuccess)
            return ServiceResult<string>.Fail(result.Error!);
        var value = result.Value!;
        if (!value.Success || string.IsNullOrEmpty(value.Id))
            return ServiceResult<string>.Fail(
                FormatErrors(value.Errors) ?? $"Salesforce {label} create reported failure.");
        return ServiceResult<string>.Ok(value.Id!);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            // Salesforce returns errors as a JSON array, not a single envelope.
            var errors = await response.Content.ReadFromJsonAsync<List<SalesforceError>>(ct);
            var first = errors?.FirstOrDefault();
            if (first?.Message is not null)
                return $"Salesforce {(int)response.StatusCode} ({first.StatusCode}): {first.Message}";
        }
        catch
        {
            // Body wasn't an error array; fall back to raw.
        }
        var raw = await response.Content.ReadAsStringAsync(ct);
        return $"Salesforce {(int)response.StatusCode}: {(raw.Length > 500 ? raw[..500] + "…" : raw)}";
    }

    private static string? FormatErrors(IEnumerable<SalesforceError> errors)
    {
        var msgs = errors.Where(e => !string.IsNullOrEmpty(e.Message)).Select(e => e.Message).ToList();
        return msgs.Count == 0 ? null : string.Join("; ", msgs);
    }

    // Salesforce SOQL doesn't have a parameter-binding mechanism on the REST
    // /query surface, so we manually escape single quotes (and backslashes,
    // which precede them) per the SOQL string-literal rules.
    private static string EscapeSoql(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
