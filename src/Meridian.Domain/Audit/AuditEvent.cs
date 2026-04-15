namespace Meridian.Domain.Audit;

public class AuditEvent
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Actor { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }

    private AuditEvent() { }

    public static AuditEvent Record(
        Guid tenantId,
        string entityType,
        Guid entityId,
        string eventType,
        string actor,
        string payloadJson)
    {
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            EventType = eventType,
            Actor = actor,
            PayloadJson = payloadJson,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }
}
