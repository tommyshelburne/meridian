using Meridian.Domain.Common;

namespace Meridian.Domain.Outreach;

public class OutreachEnrollment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OpportunityId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid SequenceId { get; private set; }
    public Guid SequenceSnapshotId { get; private set; }
    public int CurrentStep { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public DateTimeOffset EnrolledAt { get; private set; }
    public DateTimeOffset? NextSendAt { get; private set; }
    public string? PausedReason { get; private set; }

    private OutreachEnrollment() { }

    public static OutreachEnrollment Create(
        Guid tenantId,
        Guid opportunityId,
        Guid contactId,
        Guid sequenceId,
        Guid sequenceSnapshotId,
        DateTimeOffset firstSendAt)
    {
        return new OutreachEnrollment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OpportunityId = opportunityId,
            ContactId = contactId,
            SequenceId = sequenceId,
            SequenceSnapshotId = sequenceSnapshotId,
            CurrentStep = 1,
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTimeOffset.UtcNow,
            NextSendAt = firstSendAt
        };
    }

    public void AdvanceStep(DateTimeOffset nextSendAt, int totalSteps)
    {
        CurrentStep++;
        if (CurrentStep > totalSteps)
        {
            Status = EnrollmentStatus.Completed;
            NextSendAt = null;
        }
        else
        {
            NextSendAt = nextSendAt;
        }
    }

    public void MarkReplied()
    {
        Status = EnrollmentStatus.Replied;
        NextSendAt = null;
    }

    public void MarkBounced()
    {
        Status = EnrollmentStatus.Bounced;
        NextSendAt = null;
    }

    public void MarkUnsubscribed()
    {
        Status = EnrollmentStatus.Unsubscribed;
        NextSendAt = null;
    }

    public void Pause(string reason)
    {
        Status = EnrollmentStatus.Paused;
        PausedReason = reason;
        NextSendAt = null;
    }

    public void Resume(DateTimeOffset nextSendAt)
    {
        Status = EnrollmentStatus.Active;
        PausedReason = null;
        NextSendAt = nextSendAt;
    }

    public bool IsSendable => Status == EnrollmentStatus.Active && NextSendAt.HasValue;
}
