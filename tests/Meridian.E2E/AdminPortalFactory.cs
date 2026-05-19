using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.E2E;

/// <summary>
/// Extends PortalFactory with a test authentication scheme (TestAdminAuthHandler)
/// that authenticates any request carrying an X-Test-TenantId header. This lets
/// cache-invalidation tests POST to admin-only SSO endpoints without a real session.
/// </summary>
public class AdminPortalFactory : PortalFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>(
                    TestAdminAuthHandler.SchemeName, _ => { });

            // Make the test scheme the default authenticator so that headers are
            // picked up before the cookie scheme is tried. The challenge scheme
            // remains Cookies so that the existing redirect-to-login behaviour is
            // preserved when no test header is present.
            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = TestAdminAuthHandler.SchemeName;
            });
        });
    }
}
