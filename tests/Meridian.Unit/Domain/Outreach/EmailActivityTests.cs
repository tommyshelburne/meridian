using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Outreach;

namespace Meridian.Unit.Domain.Outreach;

public class EmailActivityTests
{
    private static EmailActivity NewActivity() => EmailActivity.Record(
        tenantId: Guid.NewGuid(),
        enrollmentId: Guid.NewGuid(),
        contactId: Guid.NewGuid(),
        opportunityId: Guid.NewGuid(),
        stepNumber: 1,
        subject: "Subject",
        bodyText: "body",
        messageId: "msg-1");

    [Fact]
    public void RecordSuppressedReply_sets_reason_and_body_and_keeps_status_sent()
    {
        var activity = NewActivity();
        var when = DateTimeOffset.UtcNow;

        activity.RecordSuppressedReply(when, "I'm OOO until next Monday.", "out_of_office");

        activity.Status.Should().Be(EmailStatus.Sent, "suppressed replies must NOT halt the sequence");
        activity.RepliedAt.Should().Be(when);
        activity.ReplyBody.Should().Be("I'm OOO until next Monday.");
        activity.SuppressionReason.Should().Be("out_of_office");
        activity.IsSuppressed.Should().BeTrue();
    }

    [Fact]
    public void RecordSuppressedReply_with_blank_body_leaves_reply_body_null()
    {
        var activity = NewActivity();

        activity.RecordSuppressedReply(DateTimeOffset.UtcNow, body: null, "out_of_office");

        activity.ReplyBody.Should().BeNull();
        activity.SuppressionReason.Should().Be("out_of_office");
    }

    [Fact]
    public void RecordSuppressedReply_rejects_blank_reason()
    {
        var activity = NewActivity();
        var act = () => activity.RecordSuppressedReply(DateTimeOffset.UtcNow, body: null, reason: "");

        act.Should().Throw<ArgumentException>().WithMessage("*SuppressionReason*");
    }

    [Fact]
    public void IsSuppressed_false_when_reason_unset()
    {
        var activity = NewActivity();
        activity.IsSuppressed.Should().BeFalse();
        activity.SuppressionReason.Should().BeNull();
    }

    [Fact]
    public void RecordReply_does_not_set_suppression_reason()
    {
        var activity = NewActivity();
        activity.RecordReply(DateTimeOffset.UtcNow, "Sounds good.");

        activity.Status.Should().Be(EmailStatus.Replied);
        activity.SuppressionReason.Should().BeNull();
        activity.IsSuppressed.Should().BeFalse();
    }
}
