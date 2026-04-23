using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Outreach.Resend;

namespace Meridian.Portal.Outreach;

public static class ResendWebhookEndpoints
{
    public static IEndpointRouteBuilder MapResendWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/resend/{tenantId:guid}", async (
            Guid tenantId,
            HttpContext http,
            IOutboundConfigurationRepository configRepo,
            ISecretProtector protector,
            ITenantContext tenantContext,
            BounceProcessor processor,
            SvixSignatureVerifier verifier,
            CancellationToken ct) =>
        {
            tenantContext.SetTenant(tenantId);

            var config = await configRepo.GetByTenantIdAsync(tenantId, ct);
            if (config is null || string.IsNullOrEmpty(config.EncryptedWebhookSecret))
                return Results.NotFound();

            using var reader = new StreamReader(http.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { error = "Empty payload." });

            var svixId = http.Request.Headers["svix-id"].ToString();
            var svixTimestamp = http.Request.Headers["svix-timestamp"].ToString();
            var svixSignature = http.Request.Headers["svix-signature"].ToString();

            string signingSecret;
            try
            {
                signingSecret = protector.Unprotect(config.EncryptedWebhookSecret);
            }
            catch (Exception)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            if (!verifier.Verify(signingSecret, svixId, svixTimestamp, svixSignature, body, DateTimeOffset.UtcNow))
                return Results.Unauthorized();

            var bounceEvent = ResendWebhookParser.Parse(body);
            if (bounceEvent is null)
                return Results.Accepted(); // Event type we don't act on; ack so Resend doesn't retry.

            var result = await processor.ProcessAsync(tenantId, new[] { bounceEvent }, ct);
            return result.IsSuccess
                ? Results.Accepted()
                : Results.Problem(result.Error);
        }).DisableAntiforgery();

        return app;
    }
}
