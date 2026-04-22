using System.Net;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure.Outreach;

public class ComplianceFooterEmailSender : IEmailSender
{
    private const string FooterMarker = "<!--meridian-compliance-footer-->";

    private readonly IEmailSender _inner;
    private readonly EmailComplianceOptions _options;

    public ComplianceFooterEmailSender(IEmailSender inner, IOptions<EmailComplianceOptions> options)
    {
        _inner = inner;
        _options = options.Value;
    }

    public Task<ServiceResult<SendResult>> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var withFooter = AppendFooter(message);
        return _inner.SendAsync(withFooter, ct);
    }

    private EmailMessage AppendFooter(EmailMessage message)
    {
        if (message.BodyHtml.Contains(FooterMarker, StringComparison.Ordinal))
            return message;

        var unsubscribeUrl = BuildUnsubscribeUrl(message.To);
        var footer = $"""

            {FooterMarker}
            <hr style="border:none;border-top:1px solid #ccc;margin-top:24px;">
            <p style="color:#666;font-size:12px;line-height:1.4;margin-top:12px;">
              {WebUtility.HtmlEncode(_options.PhysicalAddress)}<br>
              <a href="{unsubscribeUrl}">Unsubscribe</a> from future messages.
            </p>
            """;

        return message with { BodyHtml = message.BodyHtml + footer };
    }

    private string BuildUnsubscribeUrl(string recipient)
    {
        var separator = _options.UnsubscribeBaseUrl.Contains('?') ? "&" : "?";
        return $"{_options.UnsubscribeBaseUrl}{separator}email={WebUtility.UrlEncode(recipient)}";
    }
}
