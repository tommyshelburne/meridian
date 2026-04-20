using System.Security.Claims;
using Meridian.Application.Auth;

namespace Meridian.Portal.Auth;

public static class ClaimsBuilder
{
    public const string TenantIdClaim = "tenant_id";
    public const string TenantSlugClaim = "tenant_slug";

    public static ClaimsPrincipal Build(LoginResult login, LoginTenantMembership selected)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, login.UserId!.Value.ToString()),
            new(ClaimTypes.Email, login.Email!),
            new(ClaimTypes.Name, login.FullName ?? login.Email!),
            new(TenantIdClaim, selected.TenantId.ToString()),
            new(TenantSlugClaim, selected.TenantSlug),
            new(ClaimTypes.Role, selected.Role.ToString())
        };
        var identity = new ClaimsIdentity(claims, "MeridianCookie", ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }
}
