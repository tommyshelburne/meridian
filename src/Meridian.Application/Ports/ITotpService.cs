namespace Meridian.Application.Ports;

public record TotpEnrollment(string Secret, string ProvisioningUri);

public interface ITotpService
{
    TotpEnrollment GenerateEnrollment(string userEmail, string issuer);
    bool VerifyCode(string secret, string code);
}
