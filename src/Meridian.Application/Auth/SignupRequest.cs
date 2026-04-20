namespace Meridian.Application.Auth;

public record SignupRequest(
    string Email,
    string FullName,
    string Password,
    string TenantName,
    string TenantSlug);
