using System.Text.Json;
using Meridian.Application.Sources;
using Meridian.Domain.Sources;
using Meridian.Portal.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Sources;

public static class SourceEndpoints
{
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/app/{slug}/sources").RequireAuthorization().DisableAntiforgery();

        group.MapPost("/create", async (
            string slug, HttpContext http,
            [FromForm] CreateSourceForm form,
            SourceManagementService svc, CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");
            if (!Enum.TryParse<SourceAdapterType>(form.AdapterType, out var type))
                return Redirect(slug, error: "Invalid adapter type.");

            var result = await svc.CreateAsync(tenantId, type, form.Name,
                form.ParametersJson ?? "{}", form.Schedule, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/sources?created={result.Value!.Id}")
                : Redirect(slug, error: result.Error!);
        });

        group.MapPost("/new/rss", async (
            string slug, HttpContext http,
            [FromForm] RssWizardForm form,
            SourceManagementService svc, CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            var parameters = new
            {
                feedUrl = form.FeedUrl,
                agencyName = form.AgencyName,
                agencyState = string.IsNullOrWhiteSpace(form.AgencyState) ? null : form.AgencyState,
                isDefense = form.IsDefense == "on",
                includeKeywords = SplitCsv(form.IncludeKeywords),
                excludeKeywords = SplitCsv(form.ExcludeKeywords)
            };
            var json = JsonSerializer.Serialize(parameters);
            var result = await svc.CreateAsync(tenantId, SourceAdapterType.GenericRss,
                form.Name, json, form.Schedule, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/sources?created={result.Value!.Id}")
                : Results.Redirect($"/app/{slug}/sources/new/rss?error={Uri.EscapeDataString(result.Error!)}");
        });

        group.MapPost("/new/rest", async (
            string slug, HttpContext http,
            [FromForm] RestWizardForm form,
            SourceManagementService svc, CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            var parameters = new
            {
                url = form.Url,
                agencyName = form.AgencyName,
                agencyState = string.IsNullOrWhiteSpace(form.AgencyState) ? null : form.AgencyState,
                isDefense = form.IsDefense == "on",
                method = form.Method ?? "GET",
                headers = ParseHeaders(form.HeadersText),
                requestBody = string.IsNullOrWhiteSpace(form.RequestBody) ? null : form.RequestBody,
                resultsJsonPath = string.IsNullOrWhiteSpace(form.ResultsJsonPath) ? "$" : form.ResultsJsonPath,
                fieldMap = new
                {
                    externalId = form.MapExternalId,
                    title = form.MapTitle,
                    description = form.MapDescription,
                    postedDate = form.MapPostedDate,
                    responseDeadline = form.MapResponseDeadline,
                    naicsCode = form.MapNaicsCode,
                    estimatedValue = form.MapEstimatedValue
                }
            };
            var json = JsonSerializer.Serialize(parameters);
            var result = await svc.CreateAsync(tenantId, SourceAdapterType.GenericRest,
                form.Name, json, form.Schedule, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/sources?created={result.Value!.Id}")
                : Results.Redirect($"/app/{slug}/sources/new/rest?error={Uri.EscapeDataString(result.Error!)}");
        });

        group.MapPost("/new/webhook", async (
            string slug, HttpContext http,
            [FromForm] WebhookWizardForm form,
            SourceManagementService svc, CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            var secret = string.IsNullOrWhiteSpace(form.Secret)
                ? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
                : form.Secret;

            var parameters = new
            {
                secret,
                agencyName = form.AgencyName,
                agencyState = string.IsNullOrWhiteSpace(form.AgencyState) ? null : form.AgencyState,
                isDefense = form.IsDefense == "on",
                fieldMap = new
                {
                    externalId = form.MapExternalId,
                    title = form.MapTitle,
                    description = form.MapDescription,
                    postedDate = form.MapPostedDate,
                    responseDeadline = form.MapResponseDeadline,
                    naicsCode = form.MapNaicsCode,
                    estimatedValue = form.MapEstimatedValue
                }
            };
            var json = JsonSerializer.Serialize(parameters);
            var result = await svc.CreateAsync(tenantId, SourceAdapterType.InboundWebhook,
                form.Name, json, form.Schedule, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/sources?created={result.Value!.Id}&webhookSecret={Uri.EscapeDataString(secret)}&webhookSourceId={result.Value.Id}")
                : Results.Redirect($"/app/{slug}/sources/new/webhook?error={Uri.EscapeDataString(result.Error!)}");
        });

        group.MapPost("/{sourceId:guid}/update", async (
            string slug, Guid sourceId, HttpContext http,
            [FromForm] UpdateSourceForm form,
            SourceManagementService svc, CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            var result = await svc.UpdateParametersAsync(tenantId, sourceId,
                form.ParametersJson ?? "{}", form.Schedule, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/sources?updated={sourceId}")
                : Redirect(slug, error: result.Error!);
        });

        group.MapPost("/{sourceId:guid}/enable", async (
            string slug, Guid sourceId, HttpContext http,
            SourceManagementService svc, CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");
            var result = await svc.EnableAsync(tenantId, sourceId, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/sources")
                : Redirect(slug, error: result.Error!);
        });

        group.MapPost("/{sourceId:guid}/disable", async (
            string slug, Guid sourceId, HttpContext http,
            SourceManagementService svc, CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");
            var result = await svc.DisableAsync(tenantId, sourceId, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/sources")
                : Redirect(slug, error: result.Error!);
        });

        return app;
    }

    private static IResult Redirect(string slug, string error) =>
        Results.Redirect($"/app/{slug}/sources?error={Uri.EscapeDataString(error)}");

    private static bool TryResolveTenantId(HttpContext http, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var claim = http.User.FindFirst(ClaimsBuilder.TenantIdClaim)?.Value;
        return Guid.TryParse(claim, out tenantId);
    }

    private static string[] SplitCsv(string? input) =>
        string.IsNullOrWhiteSpace(input)
            ? Array.Empty<string>()
            : input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Dictionary<string, string>? ParseHeaders(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || colon == line.Length - 1) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length > 0) result[key] = value;
        }
        return result.Count == 0 ? null : result;
    }
}

public record CreateSourceForm(string AdapterType, string Name, string? ParametersJson, string? Schedule);
public record UpdateSourceForm(string? ParametersJson, string? Schedule);

public record RssWizardForm(
    string Name, string FeedUrl, string AgencyName,
    string? AgencyState, string? IsDefense,
    string? IncludeKeywords, string? ExcludeKeywords,
    string? Schedule);

public record RestWizardForm(
    string Name, string Url, string AgencyName,
    string? AgencyState, string? IsDefense,
    string? Method, string? HeadersText, string? RequestBody,
    string? ResultsJsonPath,
    string MapExternalId, string MapTitle,
    string? MapDescription, string? MapPostedDate, string? MapResponseDeadline,
    string? MapNaicsCode, string? MapEstimatedValue,
    string? Schedule);

public record WebhookWizardForm(
    string Name, string AgencyName,
    string? AgencyState, string? IsDefense,
    string? Secret,
    string MapExternalId, string MapTitle,
    string? MapDescription, string? MapPostedDate, string? MapResponseDeadline,
    string? MapNaicsCode, string? MapEstimatedValue,
    string? Schedule);
