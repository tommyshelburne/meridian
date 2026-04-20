using System.Net.Mail;

namespace Meridian.Domain.Users;

public class User
{
    public const int MaxFailedLoginAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string? PasswordHash { get; private set; }
    public DateTimeOffset? PasswordUpdatedAt { get; private set; }
    public bool EmailVerified { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public string? TotpSecret { get; private set; }
    public DateTimeOffset? TotpEnrolledAt { get; private set; }
    public UserStatus Status { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTwoFactorEnabled => TotpSecret is not null && TotpEnrolledAt is not null;
    public bool IsLockedOut => Status == UserStatus.Locked ||
        (LockedUntil.HasValue && LockedUntil.Value > DateTimeOffset.UtcNow);

    private User() { }

    public static User CreateWithPassword(string email, string fullName, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        var user = CreateCore(email, fullName);
        user.PasswordHash = passwordHash;
        user.PasswordUpdatedAt = user.CreatedAt;
        return user;
    }

    public static User CreateOidcOnly(string email, string fullName)
    {
        var user = CreateCore(email, fullName);
        user.EmailVerified = true;
        user.EmailVerifiedAt = user.CreatedAt;
        return user;
    }

    public void ChangePassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
        PasswordUpdatedAt = DateTimeOffset.UtcNow;
        FailedLoginAttempts = 0;
        LockedUntil = null;
        Touch();
    }

    public void VerifyEmail()
    {
        if (EmailVerified) return;
        EmailVerified = true;
        EmailVerifiedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void EnrollTotp(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("TOTP secret is required.", nameof(secret));
        TotpSecret = secret;
        TotpEnrolledAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void DisableTotp()
    {
        TotpSecret = null;
        TotpEnrolledAt = null;
        Touch();
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
        LastLoginAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void RecordFailedLogin()
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= MaxFailedLoginAttempts)
            LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
        Touch();
    }

    public void Disable()
    {
        Status = UserStatus.Disabled;
        Touch();
    }

    public void Reactivate()
    {
        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockedUntil = null;
        Touch();
    }

    public void Rename(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Name is required.", nameof(fullName));
        FullName = fullName.Trim();
        Touch();
    }

    private static User CreateCore(string email, string fullName)
    {
        var normalized = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Name is required.", nameof(fullName));

        var now = DateTimeOffset.UtcNow;
        return new User
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            FullName = fullName.Trim(),
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        try
        {
            var parsed = new MailAddress(email.Trim());
            return parsed.Address.ToLowerInvariant();
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Invalid email format.", nameof(email), ex);
        }
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
