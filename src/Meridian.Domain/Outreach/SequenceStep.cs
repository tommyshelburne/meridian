namespace Meridian.Domain.Outreach;

public class SequenceStep
{
    public Guid SequenceId { get; private set; }
    public int StepNumber { get; private set; }
    public int DelayDays { get; private set; }
    public Guid TemplateId { get; private set; }
    public string Subject { get; private set; } = null!;
    public TimeSpan SendWindowStart { get; private set; }
    public TimeSpan SendWindowEnd { get; private set; }
    public int SendWindowJitterMinutes { get; private set; }

    private SequenceStep() { }

    internal static SequenceStep Create(
        Guid sequenceId,
        int stepNumber,
        int delayDays,
        Guid templateId,
        string subject,
        TimeSpan sendWindowStart,
        TimeSpan sendWindowEnd,
        int jitterMinutes)
    {
        return new SequenceStep
        {
            SequenceId = sequenceId,
            StepNumber = stepNumber,
            DelayDays = delayDays,
            TemplateId = templateId,
            Subject = subject,
            SendWindowStart = sendWindowStart,
            SendWindowEnd = sendWindowEnd,
            SendWindowJitterMinutes = jitterMinutes
        };
    }
}
