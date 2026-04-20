using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Users;

namespace Meridian.Integration;

public class SignupFlowTests
{
    [Fact]
    public async Task End_to_end_signup_verify_login_succeeds()
    {
        using var fx = new IntegrationTestFixture();

        Guid tenantId;
        await using (var db = fx.NewDbContext())
        {
            var auth = fx.BuildAuthService(db);
            var signup = await auth.SignupAsync(new SignupRequest(
                "founder@acme.test", "Founder Person",
                "correct-horse-battery-staple",
                "Acme Corp", "acme"), CancellationToken.None);

            signup.IsSuccess.Should().BeTrue();
            tenantId = signup.Value!.TenantId;
        }

        fx.Emails.Sent.Should().ContainSingle(e =>
            e.Subject.Contains("Verify") && e.BodyHtml.Contains("/verify-email"));

        // Extract raw verification token from the email body link.
        var rawToken = ExtractToken(fx.Emails.Sent.Single().BodyHtml, "token=");

        await using (var db = fx.NewDbContext())
        {
            var auth = fx.BuildAuthService(db);
            var verify = await auth.VerifyEmailAsync(rawToken, CancellationToken.None);
            verify.IsSuccess.Should().BeTrue();
        }

        await using (var db = fx.NewDbContext())
        {
            var auth = fx.BuildAuthService(db);
            var login = await auth.LoginAsync(new LoginRequest(
                "founder@acme.test", "correct-horse-battery-staple", TotpCode: null),
                CancellationToken.None);

            login.Outcome.Should().Be(LoginOutcome.Success);
            login.Memberships.Should().ContainSingle(m =>
                m.TenantId == tenantId && m.Role == UserRole.Owner);
        }
    }

    [Fact]
    public async Task Login_fails_when_email_not_verified()
    {
        using var fx = new IntegrationTestFixture();

        await using var db = fx.NewDbContext();
        var auth = fx.BuildAuthService(db);

        var signup = await auth.SignupAsync(new SignupRequest(
            "new@acme.test", "New Person",
            "correct-horse-battery-staple",
            "Acme", "acme"), CancellationToken.None);
        signup.IsSuccess.Should().BeTrue();

        var login = await auth.LoginAsync(new LoginRequest(
            "new@acme.test", "correct-horse-battery-staple", null),
            CancellationToken.None);
        login.Outcome.Should().Be(LoginOutcome.EmailNotVerified);
    }

    [Fact]
    public async Task Signup_rejects_duplicate_slug()
    {
        using var fx = new IntegrationTestFixture();

        await using (var db = fx.NewDbContext())
        {
            var auth = fx.BuildAuthService(db);
            (await auth.SignupAsync(new SignupRequest(
                "a@acme.test", "A Person", "correct-horse-battery-staple",
                "Acme", "acme"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        await using (var db = fx.NewDbContext())
        {
            var auth = fx.BuildAuthService(db);
            var second = await auth.SignupAsync(new SignupRequest(
                "b@other.test", "B Person", "correct-horse-battery-staple",
                "Other", "acme"), CancellationToken.None);
            second.IsSuccess.Should().BeFalse();
            second.Error.Should().Contain("workspace URL");
        }
    }

    private static string ExtractToken(string html, string key)
    {
        var idx = html.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("Token not found in email body.");
        var start = idx + key.Length;
        var end = html.IndexOfAny(new[] { '"', '&', '<' }, start);
        var raw = html.Substring(start, end - start);
        return Uri.UnescapeDataString(raw);
    }
}
