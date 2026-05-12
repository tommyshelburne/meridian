using System.Text;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Outreach.Postmark;
using Microsoft.Extensions.Options;

namespace Meridian.Portal.Outreach;

public static class PostmarkInboundEndpoints
{
    public static IEndpointRouteBuilder MapPostmarkInboundEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/postmark/inbound", async (
            HttpContext http,
            IOptions<PostmarkInboundOptions> options,
            ITenantRepository tenantRepo,
            ITenantContext tenantContext,
            ReplyProcessor processor,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("PostmarkInbound");
            var opts = options.Value;

            if (string.IsNullOrEmpty(opts.Username) || string.IsNullOrEmpty(opts.Password))
            {
                logger.LogWarning("Postmark inbound webhook hit but credentials not configured.");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (!TryReadBasicAuth(http, out var providedUser, out var providedPass)
                || !FixedTimeEquals(providedUser, opts.Username)
                || !FixedTimeEquals(providedPass, opts.Password))
                return Results.Unauthorized();

            using var reader = new StreamReader(http.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { error = "Empty payload." });

            var envelope = PostmarkInboundParser.Parse(body);
            if (envelope is null)
            {
                logger.LogWarning("Postmark inbound payload could not be parsed.");
                return Results.Accepted();
            }

            if (string.IsNullOrEmpty(envelope.MailboxHash))
            {
                logger.LogWarning(
                    "Postmark inbound to {To} has no MailboxHash; cannot route to tenant.",
                    envelope.ToAddress);
                return Results.Accepted();
            }

            var tenant = await tenantRepo.GetBySlugAsync(envelope.MailboxHash, ct);
            if (tenant is null)
            {
                logger.LogWarning(
                    "Postmark inbound to {To} (hash={Hash}) — no tenant matches slug.",
                    envelope.ToAddress, envelope.MailboxHash);
                return Results.Accepted();
            }

            tenantContext.SetTenant(tenant.Id);

            var result = await processor.ProcessAsync(tenant.Id, new[] { envelope.Reply }, ct);
            return result.IsSuccess
                ? Results.Accepted()
                : Results.Problem(result.Error);
        }).DisableAntiforgery();

        return app;
    }

    private static bool TryReadBasicAuth(HttpContext http, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        var header = http.Request.Headers.Authorization.ToString();
        const string prefix = "Basic ";
        if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var encoded = header[prefix.Length..].Trim();
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var colon = decoded.IndexOf(':');
            if (colon <= 0) return false;
            username = decoded[..colon];
            password = decoded[(colon + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
