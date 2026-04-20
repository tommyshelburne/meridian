using System.Security.Cryptography;
using System.Text;
using Meridian.Application.Ports;
using Microsoft.IdentityModel.Tokens;

namespace Meridian.Infrastructure.Auth;

public class TokenHasher : ITokenHasher
{
    public string Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token is required.", nameof(token));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string GenerateToken(int byteLength = 32)
    {
        if (byteLength < 16) throw new ArgumentOutOfRangeException(nameof(byteLength));
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base64UrlEncoder.Encode(bytes);
    }
}
