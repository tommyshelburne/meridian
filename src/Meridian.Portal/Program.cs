using Meridian.Infrastructure;
using Meridian.Infrastructure.Persistence;
using Meridian.Portal.Auth;
using Meridian.Portal.Auth.Oidc;
using Meridian.Portal.Components;
using Meridian.Portal.Crm;
using Meridian.Portal.Help;
using Meridian.Portal.Ingestion;
using Meridian.Portal.Opportunities;
using Meridian.Portal.Outreach;
using Meridian.Portal.Sources;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Meridian")
    ?? throw new InvalidOperationException("ConnectionStrings:Meridian is not configured.");

builder.Services.AddMeridianInfrastructure(connectionString, builder.Configuration);
// SetApplicationName must match the Worker so that, in production, both
// hosts can read the same shared keyring (PersistKeysToFileSystem against
// a shared volume). Without matching names the Worker can't decrypt
// secrets the Portal wrote.
var dpBuilder = builder.Services.AddDataProtection().SetApplicationName("Meridian");
var dpKeyDir = builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrWhiteSpace(dpKeyDir))
    dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(dpKeyDir));

// Register the OIDC PostConfigureOptions BEFORE AddOpenIdConnect so it runs before
// the framework's validating PostConfigureOptions (which throws if Authority/ClientId
// are missing). Service collection iteration honors registration order.
builder.Services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, OidcOptionsConfigurer>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "meridian_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/not-found";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    // Template registration: hooks up OpenIdConnectHandler + the framework's options
    // configurers. The "__oidc-template" scheme is never used directly — real schemes
    // are manufactured per-tenant by DynamicOidcSchemeProvider with names of the form
    // "oidc:{tenantId}:{providerKey}". Placeholder Authority/ClientId/ClientSecret are
    // here only so the framework's validating PostConfigure doesn't throw at startup
    // when it iterates registered schemes.
    .AddOpenIdConnect("__oidc-template", options =>
    {
        options.Authority = "https://placeholder.invalid";
        options.ClientId = "__placeholder";
        options.ClientSecret = "__placeholder";
    });

builder.Services.Replace(ServiceDescriptor.Singleton<IAuthenticationSchemeProvider, DynamicOidcSchemeProvider>());

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<HelpArticleService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Apply EF migrations on startup. Idempotent: a fresh DB gets the full schema,
// an up-to-date DB is a no-op, anything in between brings forward only the
// pending migrations. Production-safe; dev convenient. Skipped under the
// "Testing" environment, where the WebApplicationFactory swaps the DbContext
// provider for InMemory and runs EnsureCreated itself.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantClaimMiddleware>();

app.MapAuthEndpoints();
app.MapCrmOAuthEndpoints();
app.MapCrmSettingsEndpoints();
app.MapSsoSettingsEndpoints();
app.MapWorkspaceEndpoints();
app.MapWebhookIngestEndpoints();
app.MapResendWebhookEndpoints();
app.MapPostmarkInboundEndpoints();
app.MapOutboundConfigEndpoints();
app.MapSequenceEndpoints();
app.MapOpportunityEndpoints();
app.MapEnrichmentEndpoints();
app.MapDevSeedEndpoints(app.Environment);
app.MapSourceEndpoints();

// Root route: redirect unauth → /login, authed → /app/{slug}/dashboard.
// Replaces the default Blazor template Home page that shipped with the
// scaffold. Must come BEFORE MapRazorComponents so it preempts the router.
app.MapGet("/", (HttpContext http) =>
{
    if (http.User.Identity?.IsAuthenticated != true)
        return Results.Redirect("/login");
    var slug = http.User.FindFirst(Meridian.Portal.Auth.ClaimsBuilder.TenantSlugClaim)?.Value;
    return string.IsNullOrEmpty(slug)
        ? Results.Redirect("/login")
        : Results.Redirect($"/app/{slug}/dashboard");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
