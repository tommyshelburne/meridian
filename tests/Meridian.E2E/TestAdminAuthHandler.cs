using System.Security.Claims;
using System.Text.Encodings.Web;
using Meridian.Portal.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.E2E;

/// <summary>
/// Authenticates requests that carry the X-Test-TenantId header, returning an
/// Owner-role principal with the supplied tenant claim. Used by AdminPortalFactory
/// to let E2E tests hit admin-only endpoints without a real cookie session.
/// </summary>
public class TestAdminAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAdmin";

    public TestAdminAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-TenantId", out var tenantIdValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var tenantIdStr = tenantIdValues.ToString();
        if (!Guid.TryParse(tenantIdStr, out _))
            return Task.FromResult(AuthenticateResult.NoResult());

        var slug = Request.Headers.TryGetValue("X-Test-TenantSlug", out var slugValues)
            ? slugValues.ToString()
            : "test";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "admin@test.example"),
            new Claim(ClaimTypes.Name, "Test Admin"),
            new Claim(ClaimsBuilder.TenantIdClaim, tenantIdStr),
            new Claim(ClaimsBuilder.TenantSlugClaim, slug),
            new Claim(ClaimTypes.Role, "Owner")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
