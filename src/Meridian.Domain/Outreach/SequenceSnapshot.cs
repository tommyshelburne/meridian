namespace Meridian.Domain.Outreach;

public class SequenceSnapshot
{
    public Guid Id { get; private set; }
    public Guid SequenceId { get; private set; }
    public string SnapshotJson { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private SequenceSnapshot() { }

    public static SequenceSnapshot Capture(Guid sequenceId, string snapshotJson)
    {
        return new SequenceSnapshot
        {
            Id = Guid.NewGuid(),
            SequenceId = sequenceId,
            SnapshotJson = snapshotJson,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
