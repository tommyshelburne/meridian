using System.Text.RegularExpressions;

namespace Meridian.Domain.Tenants;

public class Tenant
{
    private static readonly Regex SlugPattern = new("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.Compiled);

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public PlanTier Plan { get; private set; }
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Tenant() { }

    public static Tenant Create(string name, string slug, PlanTier plan = PlanTier.Trial)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));

        ValidateSlug(slug);

        var now = DateTimeOffset.UtcNow;
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.ToLowerInvariant(),
            Plan = plan,
            Status = plan == PlanTier.Trial ? TenantStatus.Trial : TenantStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));
        Name = name.Trim();
        Touch();
    }

    public void ChangePlan(PlanTier plan)
    {
        Plan = plan;
        if (plan != PlanTier.Trial && Status == TenantStatus.Trial)
            Status = TenantStatus.Active;
        Touch();
    }

    public void Activate()
    {
        Status = TenantStatus.Active;
        Touch();
    }

    public void Suspend()
    {
        Status = TenantStatus.Suspended;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static void ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Tenant slug is required.", nameof(slug));
        if (!SlugPattern.IsMatch(slug))
            throw new ArgumentException(
                "Slug must be 3-64 chars, lowercase alphanumerics and hyphens, not starting or ending with a hyphen.",
                nameof(slug));
    }
}
