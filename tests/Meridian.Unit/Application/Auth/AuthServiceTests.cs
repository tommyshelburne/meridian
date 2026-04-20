using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Users;
using OtpNet;

namespace Meridian.Unit.Application.Auth;

public class AuthServiceTests
{
    private static SignupRequest ValidSignup(string email = "owner@acme.test", string slug = "acme") =>
        new(email, "Owner Name", "correct-horse-battery", "Acme Inc", slug);

    [Fact]
    public async Task Signup_creates_tenant_user_membership_and_sends_verification()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();

        var result = await auth.SignupAsync(ValidSignup(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.Tenants.Items.Should().HaveCount(1);
        fx.Users.Items.Should().HaveCount(1);
        fx.Memberships.Items.Should().ContainSingle(m =>
            m.Role == UserRole.Owner && m.Status == MembershipStatus.Active);
        fx.AuthTokens.Verifications.Should().HaveCount(1);
        fx.Emails.Sent.Should().ContainSingle(e => e.Subject.Contains("Verify"));
    }

    [Fact]
    public async Task Signup_rejects_short_password()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();

        var result = await auth.SignupAsync(
            new SignupRequest("a@b.test", "A B", "short", "Acme", "acme"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        fx.Users.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Signup_rejects_duplicate_email()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup("owner@acme.test", "acme"), CancellationToken.None);

        var second = await auth.SignupAsync(
            ValidSignup("owner@acme.test", "other"),
            CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Contain("account already exists");
    }

    [Fact]
    public async Task Signup_rejects_taken_slug()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup("a@acme.test", "acme"), CancellationToken.None);

        var second = await auth.SignupAsync(
            ValidSignup("b@acme.test", "acme"),
            CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Login_succeeds_after_signup_and_verification()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        var signup = await auth.SignupAsync(ValidSignup(), CancellationToken.None);
        fx.Users.Items[0].VerifyEmail();

        var result = await auth.LoginAsync(
            new LoginRequest("owner@acme.test", "correct-horse-battery", null),
            CancellationToken.None);

        result.Outcome.Should().Be(LoginOutcome.Success);
        result.UserId.Should().Be(signup.Value!.UserId);
        result.Memberships.Should().ContainSingle(m => m.Role == UserRole.Owner);
    }

    [Fact]
    public async Task Login_returns_EmailNotVerified_when_not_verified()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup(), CancellationToken.None);

        var result = await auth.LoginAsync(
            new LoginRequest("owner@acme.test", "correct-horse-battery", null),
            CancellationToken.None);

        result.Outcome.Should().Be(LoginOutcome.EmailNotVerified);
    }

    [Fact]
    public async Task Login_returns_InvalidCredentials_for_wrong_password_and_increments_failure()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup(), CancellationToken.None);
        fx.Users.Items[0].VerifyEmail();

        var result = await auth.LoginAsync(
            new LoginRequest("owner@acme.test", "wrong-password-xyz", null),
            CancellationToken.None);

        result.Outcome.Should().Be(LoginOutcome.InvalidCredentials);
        fx.Users.Items[0].FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Login_locks_out_after_repeated_failures()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup(), CancellationToken.None);
        fx.Users.Items[0].VerifyEmail();

        for (var i = 0; i < User.MaxFailedLoginAttempts; i++)
            await auth.LoginAsync(new LoginRequest("owner@acme.test", "wrong", null), CancellationToken.None);

        var next = await auth.LoginAsync(
            new LoginRequest("owner@acme.test", "correct-horse-battery", null),
            CancellationToken.None);

        next.Outcome.Should().Be(LoginOutcome.LockedOut);
    }

    [Fact]
    public async Task Login_requires_totp_when_enrolled_then_succeeds_with_valid_code()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup(), CancellationToken.None);
        var user = fx.Users.Items[0];
        user.VerifyEmail();
        var enrollment = fx.Totp.GenerateEnrollment(user.Email, "Meridian");
        user.EnrollTotp(enrollment.Secret);

        var missing = await auth.LoginAsync(
            new LoginRequest("owner@acme.test", "correct-horse-battery", null),
            CancellationToken.None);
        missing.Outcome.Should().Be(LoginOutcome.TwoFactorRequired);

        var code = new Totp(Base32Encoding.ToBytes(enrollment.Secret)).ComputeTotp();
        var ok = await auth.LoginAsync(
            new LoginRequest("owner@acme.test", "correct-horse-battery", code),
            CancellationToken.None);
        ok.Outcome.Should().Be(LoginOutcome.Success);
    }

    [Fact]
    public async Task VerifyEmail_with_issued_token_marks_user_verified()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup(), CancellationToken.None);
        var rawToken = ExtractTokenFromEmail(fx.Emails.Sent.Single(e => e.Subject.Contains("Verify")).BodyHtml);

        var result = await auth.VerifyEmailAsync(rawToken, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.Users.Items[0].EmailVerified.Should().BeTrue();
        fx.AuthTokens.Verifications[0].IsConsumed.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyEmail_rejects_invalid_token()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();

        var result = await auth.VerifyEmailAsync("not-a-real-token", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RequestPasswordReset_issues_token_and_sends_email()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup(), CancellationToken.None);

        var result = await auth.RequestPasswordResetAsync("owner@acme.test", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.AuthTokens.Resets.Should().HaveCount(1);
        fx.Emails.Sent.Should().Contain(e => e.Subject.Contains("Reset"));
    }

    [Fact]
    public async Task RequestPasswordReset_is_silent_noop_for_unknown_email()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();

        var result = await auth.RequestPasswordResetAsync("ghost@nowhere.test", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.AuthTokens.Resets.Should().BeEmpty();
        fx.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetPassword_updates_hash_with_valid_token()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup(), CancellationToken.None);
        var user = fx.Users.Items[0];
        var originalHash = user.PasswordHash;
        await auth.RequestPasswordResetAsync("owner@acme.test", CancellationToken.None);
        var rawToken = ExtractTokenFromEmail(fx.Emails.Sent.Last().BodyHtml);

        var result = await auth.ResetPasswordAsync(rawToken, "brand-new-password-123", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().NotBe(originalHash);
        fx.AuthTokens.Resets[0].IsConsumed.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_rejects_short_password()
    {
        var fx = new AuthServiceTestFixture();
        var auth = fx.BuildService();
        await auth.SignupAsync(ValidSignup(), CancellationToken.None);
        await auth.RequestPasswordResetAsync("owner@acme.test", CancellationToken.None);
        var rawToken = ExtractTokenFromEmail(fx.Emails.Sent.Last().BodyHtml);

        var result = await auth.ResetPasswordAsync(rawToken, "short", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    private static string ExtractTokenFromEmail(string body)
    {
        var marker = "token=";
        var idx = body.IndexOf(marker, StringComparison.Ordinal);
        idx.Should().BeGreaterThan(-1);
        var start = idx + marker.Length;
        var end = body.IndexOf('"', start);
        return Uri.UnescapeDataString(body.Substring(start, end - start));
    }
}
