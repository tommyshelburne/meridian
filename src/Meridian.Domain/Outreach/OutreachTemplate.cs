namespace Meridian.Domain.Outreach;

public class OutreachTemplate
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string SubjectTemplate { get; private set; } = null!;
    public string BodyTemplate { get; private set; } = null!;
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ModifiedAt { get; private set; }

    private OutreachTemplate() { }

    public static OutreachTemplate Create(Guid tenantId, string name, string subjectTemplate, string bodyTemplate)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name is required.", nameof(name));

        return new OutreachTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            SubjectTemplate = subjectTemplate,
            BodyTemplate = bodyTemplate,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(string subjectTemplate, string bodyTemplate)
    {
        SubjectTemplate = subjectTemplate;
        BodyTemplate = bodyTemplate;
        Version++;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}
