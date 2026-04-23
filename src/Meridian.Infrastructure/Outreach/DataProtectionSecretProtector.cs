using Meridian.Application.Ports;
using Microsoft.AspNetCore.DataProtection;

namespace Meridian.Infrastructure.Outreach;

public class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "Meridian.OutboundConfiguration.ApiKey.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
