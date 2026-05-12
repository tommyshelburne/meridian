using FluentAssertions;
using Meridian.Infrastructure.Outreach.Postmark;

namespace Meridian.Unit.Infrastructure.Outreach.Postmark;

public class PostmarkInboundParserTests
{
    [Fact]
    public void Parses_mailbox_hash_from_top_level_field()
    {
        const string body = """
        {
          "From": "alice@example.com",
          "To": "replies+acme@meridian.app",
          "ToFull": [{ "Email": "replies+acme@meridian.app", "MailboxHash": "acme" }],
          "MailboxHash": "acme",
          "Subject": "Re: Opportunity",
          "Date": "Mon, 11 May 2026 14:30:00 +0000",
          "TextBody": "Sounds good",
          "Headers": [
            { "Name": "In-Reply-To", "Value": "<original-msg-id@resend.dev>" }
          ]
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope.Should().NotBeNull();
        envelope!.MailboxHash.Should().Be("acme");
        envelope.ToAddress.Should().Be("replies+acme@meridian.app");
        envelope.Reply.FromAddress.Should().Be("alice@example.com");
        envelope.Reply.Subject.Should().Be("Re: Opportunity");
        envelope.Reply.MessageId.Should().Be("original-msg-id@resend.dev");
    }

    [Fact]
    public void Falls_back_to_ToFull_when_top_level_MailboxHash_missing()
    {
        const string body = """
        {
          "From": "alice@example.com",
          "ToFull": [{ "Email": "replies+widgets@meridian.app", "MailboxHash": "widgets" }],
          "Subject": "Re: x",
          "Headers": []
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.MailboxHash.Should().Be("widgets");
        envelope.ToAddress.Should().Be("replies+widgets@meridian.app");
    }

    [Fact]
    public void Prefers_StrippedTextReply_over_TextBody()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: x",
          "TextBody": "Full body with quoted history\n> original message",
          "StrippedTextReply": "Just the reply",
          "Headers": []
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.Body.Should().Be("Just the reply");
    }

    [Fact]
    public void Falls_back_to_TextBody_when_StrippedTextReply_missing()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: x",
          "TextBody": "Whole message",
          "Headers": []
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.Body.Should().Be("Whole message");
    }

    [Fact]
    public void In_reply_to_header_strips_angle_brackets()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: x",
          "Headers": [
            { "Name": "In-Reply-To", "Value": "  <msg-123@example.com>  " }
          ]
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.MessageId.Should().Be("msg-123@example.com");
    }

    [Fact]
    public void Falls_back_to_References_first_entry_when_in_reply_to_absent()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: x",
          "Headers": [
            { "Name": "References", "Value": "<orig@example.com> <reply1@example.com>" }
          ]
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.MessageId.Should().Be("orig@example.com");
    }

    [Fact]
    public void Header_name_lookup_is_case_insensitive()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: x",
          "Headers": [
            { "Name": "in-reply-to", "Value": "<lower@case.com>" }
          ]
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.MessageId.Should().Be("lower@case.com");
    }

    [Fact]
    public void Missing_in_reply_to_yields_empty_message_id()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: x",
          "Headers": []
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.MessageId.Should().BeEmpty();
    }

    [Fact]
    public void Parses_date_into_received_at()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: x",
          "Date": "Mon, 11 May 2026 14:30:00 +0000",
          "Headers": []
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.ReceivedAt.Should().Be(new DateTimeOffset(2026, 5, 11, 14, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Malformed_date_falls_back_to_utc_now()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: x",
          "Date": "not a real date",
          "Headers": []
        }
        """;

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var envelope = PostmarkInboundParser.Parse(body);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        envelope!.Reply.ReceivedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void Returns_null_for_empty_body()
    {
        PostmarkInboundParser.Parse("").Should().BeNull();
        PostmarkInboundParser.Parse("   ").Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_invalid_json()
    {
        PostmarkInboundParser.Parse("{not json").Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_non_object_root()
    {
        PostmarkInboundParser.Parse("[]").Should().BeNull();
        PostmarkInboundParser.Parse("\"hello\"").Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_no_recipient_resolvable()
    {
        const string body = """
        { "From": "a@b.com", "Subject": "Re: x", "Headers": [] }
        """;

        PostmarkInboundParser.Parse(body).Should().BeNull();
    }

    [Theory]
    [InlineData("auto-replied", true)]
    [InlineData("auto-generated", true)]
    [InlineData("auto-replied; from=vacation@example.com", true)]
    [InlineData("no", false)]
    public void Auto_submitted_header_classifies_auto_reply(string value, bool expected)
    {
        var body = $$"""
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: Quick question",
          "Headers": [ { "Name": "Auto-Submitted", "Value": "{{value}}" } ]
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.IsAutoReply.Should().Be(expected);
    }

    [Fact]
    public void X_autoreply_header_classifies_auto_reply()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: Quick question",
          "Headers": [ { "Name": "X-Autoreply", "Value": "yes" } ]
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.IsAutoReply.Should().BeTrue();
    }

    [Theory]
    [InlineData("auto_reply", true)]
    [InlineData("auto-reply", true)]
    [InlineData("bulk", true)]
    [InlineData("list", true)]
    [InlineData("first-class", false)]
    public void Precedence_header_classifies_auto_reply(string value, bool expected)
    {
        var body = $$"""
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: Quick question",
          "Headers": [ { "Name": "Precedence", "Value": "{{value}}" } ]
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.IsAutoReply.Should().Be(expected);
    }

    [Theory]
    [InlineData("Out of Office", true)]
    [InlineData("Out of the office until Monday", true)]
    [InlineData("Automatic reply: I am away", true)]
    [InlineData("Auto-Reply: vacation", true)]
    [InlineData("Auto Reply", true)]
    [InlineData("Away from my desk", true)]
    [InlineData("Re: Out of Office last week", false)]
    [InlineData("Re: Contact Center RFP", false)]
    public void Subject_pattern_classifies_auto_reply(string subject, bool expected)
    {
        var body = $$"""
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "{{subject}}",
          "Headers": []
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.IsAutoReply.Should().Be(expected);
    }

    [Fact]
    public void Normal_reply_with_no_auto_signals_is_not_classified()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "r+t@m.app", "MailboxHash": "t" }],
          "Subject": "Re: Bid opportunity",
          "Headers": [
            { "Name": "Auto-Submitted", "Value": "no" },
            { "Name": "In-Reply-To", "Value": "<x@y>" }
          ]
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope!.Reply.IsAutoReply.Should().BeFalse();
    }

    [Fact]
    public void Empty_mailbox_hash_left_empty_for_caller_to_handle()
    {
        const string body = """
        {
          "From": "a@b.com",
          "ToFull": [{ "Email": "no-tag@meridian.app", "MailboxHash": "" }],
          "Subject": "Re: x",
          "Headers": []
        }
        """;

        var envelope = PostmarkInboundParser.Parse(body);

        envelope.Should().NotBeNull();
        envelope!.MailboxHash.Should().BeEmpty();
        envelope.ToAddress.Should().Be("no-tag@meridian.app");
    }
}
