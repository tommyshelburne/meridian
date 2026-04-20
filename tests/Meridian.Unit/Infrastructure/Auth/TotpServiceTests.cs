using FluentAssertions;
using Meridian.Infrastructure.Auth;
using OtpNet;

namespace Meridian.Unit.Infrastructure.Auth;

public class TotpServiceTests
{
    private readonly TotpService _service = new();

    [Fact]
    public void GenerateEnrollment_yields_base32_secret_and_uri()
    {
        var enrollment = _service.GenerateEnrollment("user@example.com", "Meridian");

        enrollment.Secret.Should().NotBeNullOrWhiteSpace();
        enrollment.ProvisioningUri.Should().StartWith("otpauth://totp/");
        enrollment.ProvisioningUri.Should().Contain("issuer=Meridian");
        Base32Encoding.ToBytes(enrollment.Secret).Should().HaveCount(20);
    }

    [Fact]
    public void VerifyCode_accepts_current_code()
    {
        var enrollment = _service.GenerateEnrollment("user@example.com", "Meridian");
        var totp = new Totp(Base32Encoding.ToBytes(enrollment.Secret));
        var code = totp.ComputeTotp();

        _service.VerifyCode(enrollment.Secret, code).Should().BeTrue();
    }

    [Fact]
    public void VerifyCode_rejects_wrong_code()
    {
        var enrollment = _service.GenerateEnrollment("user@example.com", "Meridian");
        _service.VerifyCode(enrollment.Secret, "000000").Should().BeFalse();
    }

    [Fact]
    public void VerifyCode_rejects_empty_input()
    {
        var enrollment = _service.GenerateEnrollment("user@example.com", "Meridian");
        _service.VerifyCode(enrollment.Secret, "").Should().BeFalse();
        _service.VerifyCode("", "123456").Should().BeFalse();
    }

    [Fact]
    public void VerifyCode_rejects_non_base32_secret()
    {
        _service.VerifyCode("not-base32-!!!", "123456").Should().BeFalse();
    }
}
