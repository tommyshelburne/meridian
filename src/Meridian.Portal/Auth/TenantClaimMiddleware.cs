using Meridian.Domain.Tenants;

namespace Meridian.Portal.Auth;

public class TenantClaimMiddleware
{
    private readonly RequestDelegate _next;

    public TenantClaimMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var tenantClaim = context.User.FindFirst(ClaimsBuilder.TenantIdClaim)?.Value;
        if (!string.IsNullOrEmpty(tenantClaim) && Guid.TryParse(tenantClaim, out var tenantId))
            tenantContext.SetTenant(tenantId);
        await _next(context);
    }
}
