using Meridian.Domain.Common;

namespace Meridian.Domain.Outreach;

public class EmailActivity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid OpportunityId { get; private set; }
    public int StepNumber { get; private set; }
    public string Subject { get; private set; } = null!;
    public string BodyText { get; private set; } = null!;
    public DateTimeOffset SentAt { get; private set; }
    public string? MessageId { get; private set; }
    public EmailStatus Status { get; private set; }
    public DateTimeOffset? RepliedAt { get; private set; }
    public string? ReplyBody { get; private set; }
    public string? SuppressionReason { get; private set; }
    public DateTimeOffset? BouncedAt { get; private set; }
    public string? BouncedReason { get; private set; }

    public bool IsSuppressed => !string.IsNullOrEmpty(SuppressionReason);

    private EmailActivity() { }

    public static EmailActivity Record(
        Guid tenantId,
        Guid enrollmentId,
        Guid contactId,
        Guid opportunityId,
        int stepNumber,
        string subject,
        string bodyText,
        string? messageId)
    {
        return new EmailActivity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EnrollmentId = enrollmentId,
            ContactId = contactId,
            OpportunityId = opportunityId,
            StepNumber = stepNumber,
            Subject = subject,
            BodyText = bodyText,
            SentAt = DateTimeOffset.UtcNow,
            MessageId = messageId,
            Status = EmailStatus.Sent
        };
    }

    public void RecordReply(DateTimeOffset repliedAt, string? body = null)
    {
        Status = EmailStatus.Replied;
        RepliedAt = repliedAt;
        if (!string.IsNullOrWhiteSpace(body)) ReplyBody = body;
    }

    public void RecordSuppressedReply(DateTimeOffset repliedAt, string? body, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("SuppressionReason is required.", nameof(reason));
        RepliedAt = repliedAt;
        if (!string.IsNullOrWhiteSpace(body)) ReplyBody = body;
        SuppressionReason = reason.Trim();
    }

    public void RecordBounce(DateTimeOffset bouncedAt, string reason)
    {
        Status = EmailStatus.Bounced;
        BouncedAt = bouncedAt;
        BouncedReason = reason;
    }
}
