using FluentAssertions;
using Meridian.Application.Outreach;
using Meridian.Infrastructure.Outreach.Resend;

namespace Meridian.Unit.Infrastructure.Outreach.Resend;

public class ResendWebhookParserTests
{
    [Fact]
    public void Parses_email_bounced_with_permanent_bounce_as_hard_bounce()
    {
        const string body = """
        {
          "type": "email.bounced",
          "created_at": "2026-04-22T14:00:00Z",
          "data": {
            "email_id": "abc",
            "to": ["target@dest.com"],
            "bounce": { "message": "550 5.1.1 user unknown", "type": "Permanent", "subType": "OnAccountSuppressionList" }
          }
        }
        """;

        var evt = ResendWebhookParser.Parse(body);

        evt.Should().NotBeNull();
        evt!.Email.Should().Be("target@dest.com");
        evt.Kind.Should().Be(BounceEventKind.HardBounce);
        evt.ProviderReason.Should().Be("550 5.1.1 user unknown");
    }

    [Fact]
    public void Transient_bounce_downgraded_to_soft()
    {
        const string body = """
        {
          "type": "email.bounced",
          "data": {
            "to": ["x@y.com"],
            "bounce": { "type": "Transient", "message": "mailbox full" }
          }
        }
        """;

        var evt = ResendWebhookParser.Parse(body);

        evt!.Kind.Should().Be(BounceEventKind.SoftBounce);
    }

    [Fact]
    public void Email_complained_maps_to_spam_complaint()
    {
        const string body = """
        {
          "type": "email.complained",
          "data": { "to": ["spam@reporter.com"] }
        }
        """;

        var evt = ResendWebhookParser.Parse(body);

        evt!.Kind.Should().Be(BounceEventKind.SpamComplaint);
        evt.Email.Should().Be("spam@reporter.com");
    }

    [Theory]
    [InlineData("email.delivered")]
    [InlineData("email.opened")]
    [InlineData("email.clicked")]
    [InlineData("email.sent")]
    public void Non_actionable_event_types_return_null(string type)
    {
        var body = $$"""
        {
          "type": "{{type}}",
          "data": { "to": ["x@y.com"] }
        }
        """;

        ResendWebhookParser.Parse(body).Should().BeNull();
    }

    [Fact]
    public void Missing_recipient_returns_null()
    {
        const string body = """
        { "type": "email.bounced", "data": { "bounce": { "type": "Permanent" } } }
        """;

        ResendWebhookParser.Parse(body).Should().BeNull();
    }

    [Fact]
    public void Empty_body_returns_null()
    {
        ResendWebhookParser.Parse("").Should().BeNull();
        ResendWebhookParser.Parse("   ").Should().BeNull();
    }

    [Fact]
    public void Falls_back_to_subType_when_message_missing()
    {
        const string body = """
        {
          "type": "email.bounced",
          "data": {
            "to": ["t@d.com"],
            "bounce": { "type": "Permanent", "subType": "MailboxDoesNotExist" }
          }
        }
        """;

        var evt = ResendWebhookParser.Parse(body);
        evt!.ProviderReason.Should().Be("MailboxDoesNotExist");
    }
}
