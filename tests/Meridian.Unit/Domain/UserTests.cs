using FluentAssertions;
using Meridian.Domain.Users;

namespace Meridian.Unit.Domain;

public class UserTests
{
    [Fact]
    public void CreateWithPassword_normalizes_email_and_sets_fields()
    {
        var user = User.CreateWithPassword("  Tommy@Example.com ", "Tommy Shelburne", "hash");

        user.Email.Should().Be("tommy@example.com");
        user.FullName.Should().Be("Tommy Shelburne");
        user.PasswordHash.Should().Be("hash");
        user.PasswordUpdatedAt.Should().NotBeNull();
        user.EmailVerified.Should().BeFalse();
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void CreateOidcOnly_marks_email_verified_and_has_no_password()
    {
        var user = User.CreateOidcOnly("angel@example.com", "Angel");

        user.PasswordHash.Should().BeNull();
        user.EmailVerified.Should().BeTrue();
        user.EmailVerifiedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-email")]
    public void CreateWithPassword_rejects_invalid_email(string email)
    {
        var act = () => User.CreateWithPassword(email, "Name", "hash");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordFailedLogin_locks_user_after_max_attempts()
    {
        var user = User.CreateWithPassword("a@b.co", "A", "hash");

        for (var i = 0; i < User.MaxFailedLoginAttempts; i++)
            user.RecordFailedLogin();

        user.FailedLoginAttempts.Should().Be(User.MaxFailedLoginAttempts);
        user.LockedUntil.Should().NotBeNull();
        user.IsLockedOut.Should().BeTrue();
    }

    [Fact]
    public void RecordSuccessfulLogin_clears_failed_attempts()
    {
        var user = User.CreateWithPassword("a@b.co", "A", "hash");
        user.RecordFailedLogin();
        user.RecordFailedLogin();

        user.RecordSuccessfulLogin();

        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntil.Should().BeNull();
        user.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public void ChangePassword_updates_hash_and_resets_lockout()
    {
        var user = User.CreateWithPassword("a@b.co", "A", "hash1");
        for (var i = 0; i < User.MaxFailedLoginAttempts; i++)
            user.RecordFailedLogin();

        user.ChangePassword("hash2");

        user.PasswordHash.Should().Be("hash2");
        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void EnrollTotp_marks_2fa_enabled()
    {
        var user = User.CreateWithPassword("a@b.co", "A", "hash");

        user.EnrollTotp("secret");

        user.IsTwoFactorEnabled.Should().BeTrue();
        user.TotpSecret.Should().Be("secret");
    }

    [Fact]
    public void DisableTotp_clears_secret()
    {
        var user = User.CreateWithPassword("a@b.co", "A", "hash");
        user.EnrollTotp("secret");

        user.DisableTotp();

        user.IsTwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public void VerifyEmail_is_idempotent()
    {
        var user = User.CreateWithPassword("a@b.co", "A", "hash");

        user.VerifyEmail();
        var firstVerifiedAt = user.EmailVerifiedAt;
        user.VerifyEmail();

        user.EmailVerifiedAt.Should().Be(firstVerifiedAt);
    }
}
