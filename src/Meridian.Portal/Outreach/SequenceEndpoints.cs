using Meridian.Application.Outreach;
using Meridian.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Outreach;

public static class SequenceEndpoints
{
    public static IEndpointRouteBuilder MapSequenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/app/{slug}/settings/sequences")
            .RequireAuthorization()
            .DisableAntiforgery();

        group.MapPost("/templates/create", async (
            string slug,
            HttpContext http,
            [FromForm] TemplateCreateForm form,
            OutreachSequenceService service,
            CancellationToken ct) =>
        {
            if (!TryGetTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            var request = new CreateTemplateRequest(
                Name: form.Name ?? string.Empty,
                SubjectTemplate: form.SubjectTemplate ?? string.Empty,
                BodyTemplate: form.BodyTemplate ?? string.Empty);

            var result = await service.CreateTemplateAsync(tenantId, request, ct);
            return result.IsSuccess ? Redirect(slug, saved: "1") : Redirect(slug, error: result.Error);
        });

        group.MapPost("/create", async (
            string slug,
            HttpContext http,
            HttpRequest req,
            OutreachSequenceService service,
            CancellationToken ct) =>
        {
            if (!TryGetTenantId(http, out var tenantId))
                return Redirect(slug, error: "Session expired.");

            var form = await req.ReadFormAsync(ct);
            var name = form["Name"].ToString();
            var agencyTypeStr = form["AgencyType"].ToString();
            var oppTypeStr = form["OpportunityType"].ToString();

            if (!Enum.TryParse<AgencyType>(agencyTypeStr, out var agencyType))
                return Redirect(slug, error: "Unknown agency type.");
            if (!Enum.TryParse<OpportunityType>(oppTypeStr, out var oppType))
                return Redirect(slug, error: "Unknown opportunity type.");

            var steps = new List<CreateSequenceStepRequest>();
            // Up to 5 step rows are rendered. Skip rows where the operator left
            // TemplateId blank ("— skip this step —").
            for (var i = 0; i < 5; i++)
            {
                var templateIdStr = form[$"Steps[{i}].TemplateId"].ToString();
                if (string.IsNullOrWhiteSpace(templateIdStr)) continue;
                if (!Guid.TryParse(templateIdStr, out var templateId))
                    return Redirect(slug, error: $"Step {i + 1} has an invalid template id.");

                if (!int.TryParse(form[$"Steps[{i}].DelayDays"].ToString(), out var delayDays) || delayDays < 0)
                    return Redirect(slug, error: $"Step {i + 1} delay days must be a non-negative integer.");

                var subject = form[$"Steps[{i}].Subject"].ToString();
                var startStr = form[$"Steps[{i}].SendWindowStart"].ToString();
                var endStr = form[$"Steps[{i}].SendWindowEnd"].ToString();
                if (!TimeSpan.TryParse(string.IsNullOrWhiteSpace(startStr) ? "00:00" : startStr, out var start))
                    return Redirect(slug, error: $"Step {i + 1} has an invalid send window start.");
                if (!TimeSpan.TryParse(string.IsNullOrWhiteSpace(endStr) ? "23:59" : endStr, out var end))
                    return Redirect(slug, error: $"Step {i + 1} has an invalid send window end.");

                if (!int.TryParse(form[$"Steps[{i}].JitterMinutes"].ToString(), out var jitter) || jitter < 0)
                    jitter = 0;

                steps.Add(new CreateSequenceStepRequest(
                    delayDays, templateId, subject, start, end, jitter));
            }

            if (steps.Count == 0)
                return Redirect(slug, error: "At least one step must be configured.");

            var request = new CreateSequenceRequest(name, oppType, agencyType, steps);
            var result = await service.CreateSequenceAsync(tenantId, request, ct);
            return result.IsSuccess ? Redirect(slug, saved: "1") : Redirect(slug, error: result.Error);
        });

        return app;
    }

    private static bool TryGetTenantId(HttpContext http, out Guid tenantId)
        => Guid.TryParse(http.User.FindFirst(Auth.ClaimsBuilder.TenantIdClaim)?.Value, out tenantId);

    private static IResult Redirect(string slug, string? saved = null, string? error = null)
    {
        var query = saved is not null
            ? $"?saved={Uri.EscapeDataString(saved)}"
            : $"?error={Uri.EscapeDataString(error ?? "Unknown error.")}";
        return Results.Redirect($"/app/{slug}/settings/sequences{query}");
    }
}

public class TemplateCreateForm
{
    public string? Name { get; set; }
    public string? SubjectTemplate { get; set; }
    public string? BodyTemplate { get; set; }
}
