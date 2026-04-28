using Meridian.Application.Outreach;
using Meridian.Domain.Outreach;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Outreach;

public static class OutboundConfigEndpoints
{
    public static IEndpointRouteBuilder MapOutboundConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/app/{slug}/settings/outbound", async (
            string slug,
            HttpContext http,
            [FromForm] OutboundConfigForm form,
            OutboundConfigurationService service,
            CancellationToken ct) =>
        {
            var claim = http.User.FindFirst(Auth.ClaimsBuilder.TenantIdClaim)?.Value;
            if (!Guid.TryParse(claim, out var tenantId))
                return Results.Redirect($"/app/{slug}/settings/outbound?error={Uri.EscapeDataString("Session expired.")}");

            if (!Enum.TryParse<OutboundProviderType>(form.ProviderType, out var providerType))
                return Results.Redirect($"/app/{slug}/settings/outbound?error={Uri.EscapeDataString("Unknown provider.")}");

            int? dailyCap = null;
            if (!string.IsNullOrWhiteSpace(form.DailyCap))
            {
                if (!int.TryParse(form.DailyCap.Trim(), out var parsed) || parsed < 1)
                    return Results.Redirect($"/app/{slug}/settings/outbound?error={Uri.EscapeDataString("Daily cap must be a positive integer or blank.")}");
                dailyCap = parsed;
            }

            var request = new UpsertOutboundRequest(
                providerType,
                Blank(form.ApiKey),
                form.FromAddress ?? string.Empty,
                form.FromName ?? string.Empty,
                Blank(form.ReplyToAddress),
                form.PhysicalAddress ?? string.Empty,
                form.UnsubscribeBaseUrl ?? string.Empty,
                Blank(form.WebhookSecret),
                dailyCap);

            var result = await service.UpsertAsync(tenantId, request, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/settings/outbound?saved=1")
                : Results.Redirect($"/app/{slug}/settings/outbound?error={Uri.EscapeDataString(result.Error!)}");
        }).RequireAuthorization().DisableAntiforgery();

        return app;
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class OutboundConfigForm
{
    public string? ProviderType { get; set; }
    public string? ApiKey { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ReplyToAddress { get; set; }
    public string? PhysicalAddress { get; set; }
    public string? UnsubscribeBaseUrl { get; set; }
    public string? WebhookSecret { get; set; }
    public string? DailyCap { get; set; }
}
