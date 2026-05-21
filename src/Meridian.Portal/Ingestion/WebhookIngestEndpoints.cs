using Meridian.Application.Ports;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion.Generic;

namespace Meridian.Portal.Ingestion;

public static class WebhookIngestEndpoints
{
    public static IEndpointRouteBuilder MapWebhookIngestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/ingest/{sourceId:guid}", async (
            Guid sourceId,
            HttpContext http,
            ISourceDefinitionRepository sources,
            IWebhookIngestQueue queue,
            CancellationToken ct) =>
        {
            // External webhook callers have no Meridian session cookie, so the
            // source must be resolved across tenants (by its global id) — a
            // tenant-filtered lookup would find nothing and reject every POST.
            var source = await sources.GetByIdAcrossTenantsAsync(sourceId, ct);
            if (source is null || source.AdapterType != SourceAdapterType.InboundWebhook)
                return Results.NotFound();

            if (!source.IsEnabled)
                return Results.StatusCode(StatusCodes.Status423Locked);

            var parameters = InboundWebhookParameters.Parse(source.ParametersJson);
            if (parameters is null)
                return Results.BadRequest(new { error = "Source is misconfigured." });

            if (!http.Request.Headers.TryGetValue("X-Meridian-Secret", out var provided)
                || !string.Equals(provided.ToString(), parameters.Secret, StringComparison.Ordinal))
                return Results.Unauthorized();

            using var reader = new StreamReader(http.Request.Body);
            var json = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
                return Results.BadRequest(new { error = "Empty payload." });

            await queue.EnqueueAsync(
                new WebhookPayload(source.TenantId, source.Id, json, DateTimeOffset.UtcNow), ct);
            return Results.Accepted();
        }).DisableAntiforgery();

        return app;
    }
}
