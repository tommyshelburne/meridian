using System.Net;
using Meridian.Application.Common;
using Meridian.Application.Ports;

namespace Meridian.Infrastructure.Outreach;

public class ComplianceFooterEmailSender : IEmailSender
{
    private const string FooterMarker = "<!--meridian-compliance-footer-->";

    private readonly IEmailSender _inner;
    private readonly TenantOutboundContext _context;

    public ComplianceFooterEmailSender(IEmailSender inner, TenantOutboundContext context)
    {
        _inner = inner;
        _context = context;
    }

    public async Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var settings = await _context.GetAsync(ct);
        // No tenant config -> pass through; the downstream router will reject the send
        // with NoConfigError so nothing actually goes out without a footer.
        var prepared = settings is null ? message : AppendFooter(message, settings);
        return await _inner.SendAsync(prepared, ct);
    }

    private static EmailMessage AppendFooter(EmailMessage message, TenantOutboundSettings settings)
    {
        if (message.BodyHtml.Contains(FooterMarker, StringComparison.Ordinal))
            return message;

        var unsubscribeUrl = BuildUnsubscribeUrl(settings.UnsubscribeBaseUrl, message.To);
        var footer = $"""

            {FooterMarker}
            <hr style="border:none;border-top:1px solid #ccc;margin-top:24px;">
            <p style="color:#666;font-size:12px;line-height:1.4;margin-top:12px;">
              {WebUtility.HtmlEncode(settings.PhysicalAddress)}<br>
              <a href="{unsubscribeUrl}">Unsubscribe</a> from future messages.
            </p>
            """;

        return message with { BodyHtml = message.BodyHtml + footer };
    }

    private static string BuildUnsubscribeUrl(string baseUrl, string recipient)
    {
        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}email={WebUtility.UrlEncode(recipient)}";
    }
}
