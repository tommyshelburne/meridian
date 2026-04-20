using Meridian.Application.Ports;
using OtpNet;

namespace Meridian.Infrastructure.Auth;

public class TotpService : ITotpService
{
    private const int SecretByteLength = 20;

    public TotpEnrollment GenerateEnrollment(string userEmail, string issuer)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
            throw new ArgumentException("User email is required.", nameof(userEmail));
        if (string.IsNullOrWhiteSpace(issuer))
            throw new ArgumentException("Issuer is required.", nameof(issuer));

        var secretBytes = KeyGeneration.GenerateRandomKey(SecretByteLength);
        var base32Secret = Base32Encoding.ToString(secretBytes);
        var uri = BuildProvisioningUri(base32Secret, userEmail, issuer);
        return new TotpEnrollment(base32Secret, uri);
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            return false;
        byte[] secretBytes;
        try
        {
            secretBytes = Base32Encoding.ToBytes(secret);
        }
        catch (ArgumentException)
        {
            return false;
        }
        var totp = new Totp(secretBytes);
        return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
    }

    private static string BuildProvisioningUri(string base32Secret, string email, string issuer)
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString($"{issuer}:{email}");
        return $"otpauth://totp/{encodedAccount}?secret={base32Secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }
}
