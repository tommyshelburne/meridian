namespace Meridian.Domain.Users;

public class EmailVerificationToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsConsumed => ConsumedAt.HasValue;
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsUsable => !IsConsumed && !IsExpired;

    private EmailVerificationToken() { }

    public static EmailVerificationToken Issue(Guid userId, string tokenHash)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));

        var now = DateTimeOffset.UtcNow;
        return new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime)
        };
    }

    public void Consume()
    {
        if (IsConsumed) throw new InvalidOperationException("Token already consumed.");
        if (IsExpired) throw new InvalidOperationException("Token has expired.");
        ConsumedAt = DateTimeOffset.UtcNow;
    }
}
