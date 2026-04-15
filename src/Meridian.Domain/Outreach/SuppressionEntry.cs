namespace Meridian.Domain.Outreach;

public class SuppressionEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Value { get; private set; } = null!;
    public SuppressionType Type { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTimeOffset AddedAt { get; private set; }

    private SuppressionEntry() { }

    public static SuppressionEntry Create(Guid tenantId, string value, SuppressionType type, string reason)
    {
        return new SuppressionEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Value = value.ToLowerInvariant(),
            Type = type,
            Reason = reason,
            AddedAt = DateTimeOffset.UtcNow
        };
    }
}

public enum SuppressionType
{
    Email,
    Domain
}
