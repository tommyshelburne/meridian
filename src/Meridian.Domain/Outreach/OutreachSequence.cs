using Meridian.Domain.Common;

namespace Meridian.Domain.Outreach;

public class OutreachSequence
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public OpportunityType OpportunityType { get; private set; }
    public AgencyType AgencyType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    private readonly List<SequenceStep> _steps = new();
    public IReadOnlyCollection<SequenceStep> Steps => _steps.AsReadOnly();

    private OutreachSequence() { }

    public static OutreachSequence Create(
        Guid tenantId,
        string name,
        OpportunityType opportunityType,
        AgencyType agencyType)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sequence name is required.", nameof(name));

        return new OutreachSequence
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            OpportunityType = opportunityType,
            AgencyType = agencyType,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void AddStep(int delayDays, Guid templateId, string subject,
        TimeSpan sendWindowStart, TimeSpan sendWindowEnd, int jitterMinutes = 0)
    {
        var stepNumber = _steps.Count + 1;
        _steps.Add(SequenceStep.Create(Id, stepNumber, delayDays, templateId, subject,
            sendWindowStart, sendWindowEnd, jitterMinutes));
    }

    public void MarkUsed() => LastUsedAt = DateTimeOffset.UtcNow;
}
