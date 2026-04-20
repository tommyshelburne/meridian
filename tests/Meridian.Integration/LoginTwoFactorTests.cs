using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Tenants;
using Meridian.Domain.Users;

namespace Meridian.Integration;

public class LoginTwoFactorTests
{
    [Fact]
    public async Task Login_requires_totp_when_enrolled()
    {
        using var fx = new IntegrationTestFixture();
        var enrollment = fx.Totp.GenerateEnrollment("user@acme.test", "Meridian");

        var (userId, _) = await SeedVerifiedUserWithTotp(fx, enrollment.Secret);

        await using var db = fx.NewDbContext();
        var auth = fx.BuildAuthService(db);

        var missingCode = await auth.LoginAsync(
            new LoginRequest("user@acme.test", "correct-horse-battery-staple", null),
            CancellationToken.None);
        missingCode.Outcome.Should().Be(LoginOutcome.TwoFactorRequired);

        var badCode = await auth.LoginAsync(
            new LoginRequest("user@acme.test", "correct-horse-battery-staple", "000000"),
            CancellationToken.None);
        badCode.Outcome.Should().Be(LoginOutcome.InvalidTotp);

        var validCode = ComputeCurrentTotp(enrollment.Secret);
        var success = await auth.LoginAsync(
            new LoginRequest("user@acme.test", "correct-horse-battery-staple", validCode),
            CancellationToken.None);
        success.Outcome.Should().Be(LoginOutcome.Success);
        success.UserId.Should().Be(userId);
    }

    private static async Task<(Guid UserId, Guid TenantId)> SeedVerifiedUserWithTotp(
        IntegrationTestFixture fx, string totpSecret)
    {
        var tenant = Tenant.Create("Acme", "acme");
        var user = User.CreateWithPassword("user@acme.test", "User One",
            fx.Passwords.Hash("correct-horse-battery-staple"));
        user.VerifyEmail();
        user.EnrollTotp(totpSecret);
        var membership = UserTenant.CreateOwner(user.Id, tenant.Id);

        await using var db = fx.NewDbContext();
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.UserTenants.Add(membership);
        await db.SaveChangesAsync();
        return (user.Id, tenant.Id);
    }

    private static string ComputeCurrentTotp(string base32Secret)
    {
        var bytes = OtpNet.Base32Encoding.ToBytes(base32Secret);
        var totp = new OtpNet.Totp(bytes);
        return totp.ComputeTotp();
    }
}
