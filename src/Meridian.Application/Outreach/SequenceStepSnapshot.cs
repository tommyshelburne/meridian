namespace Meridian.Application.Outreach;

// Serialized shape of a single step inside SequenceSnapshot.SnapshotJson.
// Captured at enrollment time (snapshot-on-enrollment, spec §11.3) so later
// template edits cannot retroactively change in-flight sequences.
public record SequenceStepSnapshot(
    int StepNumber,
    int DelayDays,
    string Subject,
    string BodyTemplate,
    TimeSpan SendWindowStart,
    TimeSpan SendWindowEnd,
    int JitterMinutes);
