namespace Meridian.Application.Auth;

public record LoginRequest(string Email, string Password, string? TotpCode);
