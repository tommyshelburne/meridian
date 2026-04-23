using Meridian.Domain.Common;

namespace Meridian.Domain.Contacts;

public class Contact
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string FullName { get; private set; } = null!;
    public string? Title { get; private set; }
    public Agency Agency { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? LinkedInUrl { get; private set; }
    public ContactSource Source { get; private set; }
    public float ConfidenceScore { get; private set; }
    public bool IsOptedOut { get; private set; }
    public bool IsBounced { get; private set; }
    public int SoftBounceCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastVerifiedAt { get; private set; }

    public const int SoftBounceThreshold = 3;

    private Contact() { }

    public static Contact Create(
        Guid tenantId,
        string fullName,
        Agency agency,
        ContactSource source,
        float confidenceScore,
        string? title = null,
        string? email = null,
        string? phone = null,
        string? linkedInUrl = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Contact name is required.", nameof(fullName));

        if (confidenceScore < 0f || confidenceScore > 1f)
            throw new ArgumentOutOfRangeException(nameof(confidenceScore), "Confidence must be between 0.0 and 1.0.");

        return new Contact
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = fullName,
            Title = title,
            Agency = agency,
            Email = email,
            Phone = phone,
            LinkedInUrl = linkedInUrl,
            Source = source,
            ConfidenceScore = confidenceScore,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateEmail(string email)
    {
        Email = email;
        LastVerifiedAt = DateTimeOffset.UtcNow;
    }

    public void OptOut()
    {
        IsOptedOut = true;
    }

    public void MarkBounced()
    {
        IsBounced = true;
    }

    /// <summary>
    /// Records a transient bounce. Returns true when the threshold has been
    /// reached and the contact has been escalated to permanently bounced —
    /// caller should add suppression and stop active enrollments. Returns
    /// false when the soft bounce was just counted.
    /// </summary>
    public bool RecordSoftBounce()
    {
        if (IsBounced) return false;
        SoftBounceCount++;
        if (SoftBounceCount >= SoftBounceThreshold)
        {
            IsBounced = true;
            return true;
        }
        return false;
    }

    public void Verify()
    {
        LastVerifiedAt = DateTimeOffset.UtcNow;
    }

    public bool IsEnrollable => !IsOptedOut && !IsBounced && Email is not null && ConfidenceScore >= 0.5f;
}
