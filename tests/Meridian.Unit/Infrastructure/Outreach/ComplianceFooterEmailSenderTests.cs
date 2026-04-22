using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Infrastructure.Outreach;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure.Outreach;

public class ComplianceFooterEmailSenderTests
{
    private readonly RecordingEmailSender _inner = new();
    private readonly EmailComplianceOptions _options = new()
    {
        PhysicalAddress = "Test Co, 1 Main St, City, ST 00000",
        UnsubscribeBaseUrl = "https://example.com/u"
    };

    private ComplianceFooterEmailSender Build() =>
        new(_inner, Options.Create(_options));

    private static EmailMessage Msg(string body = "<p>Hello</p>") =>
        new("to@x.com", "from@x.com", "From", "Subject", body);

    [Fact]
    public async Task Footer_appends_physical_address()
    {
        await Build().SendAsync(Msg(), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().Contain(_options.PhysicalAddress);
    }

    [Fact]
    public async Task Footer_includes_unsubscribe_link_with_recipient_email()
    {
        await Build().SendAsync(Msg(), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().Contain("https://example.com/u?email=to%40x.com");
        sent.BodyHtml.Should().Contain("Unsubscribe");
    }

    [Fact]
    public async Task Footer_appends_only_once()
    {
        var sender = Build();
        await sender.SendAsync(Msg(), CancellationToken.None);
        var firstBody = _inner.Sent[0].BodyHtml;

        await sender.SendAsync(Msg(firstBody), CancellationToken.None);
        var secondBody = _inner.Sent[1].BodyHtml;

        secondBody.Should().Be(firstBody);
    }

    [Fact]
    public async Task Footer_handles_unsubscribe_url_with_existing_query_string()
    {
        _options.UnsubscribeBaseUrl = "https://example.com/u?source=email";
        await Build().SendAsync(Msg(), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().Contain("source=email&email=to%40x.com");
    }

    [Fact]
    public async Task Original_body_is_preserved_above_footer()
    {
        await Build().SendAsync(Msg("<p>Greetings, contact</p>"), CancellationToken.None);

        var sent = _inner.Sent.Single();
        sent.BodyHtml.Should().StartWith("<p>Greetings, contact</p>");
    }
}
