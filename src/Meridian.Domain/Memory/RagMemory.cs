namespace Meridian.Domain.Memory;

public class RagMemory
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string Content { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    // Embedding vector stored at persistence layer (pgvector float[] mapping)

    private RagMemory() { }

    public static RagMemory Create(
        Guid tenantId,
        string entityType,
        Guid entityId,
        string content)
    {
        return new RagMemory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
