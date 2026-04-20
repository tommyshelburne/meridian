namespace Meridian.Application.Auth;

public record SignupResult(Guid UserId, Guid TenantId, string TenantSlug);
