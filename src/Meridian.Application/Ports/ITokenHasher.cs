namespace Meridian.Application.Ports;

public interface ITokenHasher
{
    string Hash(string token);
    string GenerateToken(int byteLength = 32);
}
