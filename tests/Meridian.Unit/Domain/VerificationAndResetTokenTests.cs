using FluentAssertions;
using Meridian.Domain.Users;

namespace Meridian.Unit.Domain;

public class VerificationAndResetTokenTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void EmailVerificationToken_issued_is_usable()
    {
        var token = EmailVerificationToken.Issue(UserId, "hash");

        token.UserId.Should().Be(UserId);
        token.IsUsable.Should().BeTrue();
        token.IsConsumed.Should().BeFalse();
        token.ExpiresAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.Add(EmailVerificationToken.Lifetime),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EmailVerificationToken_consume_prevents_reuse()
    {
        var token = EmailVerificationToken.Issue(UserId, "hash");

        token.Consume();

        token.IsConsumed.Should().BeTrue();
        var act = () => token.Consume();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EmailVerificationToken_requires_user_and_hash()
    {
        var missingUser = () => EmailVerificationToken.Issue(Guid.Empty, "hash");
        var missingHash = () => EmailVerificationToken.Issue(UserId, "");

        missingUser.Should().Throw<ArgumentException>();
        missingHash.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PasswordResetToken_issued_has_one_hour_lifetime()
    {
        var token = PasswordResetToken.Issue(UserId, "hash");

        token.IsUsable.Should().BeTrue();
        token.ExpiresAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.Add(PasswordResetToken.Lifetime),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PasswordResetToken_consume_prevents_reuse()
    {
        var token = PasswordResetToken.Issue(UserId, "hash");

        token.Consume();

        var act = () => token.Consume();
        act.Should().Throw<InvalidOperationException>();
    }
}
