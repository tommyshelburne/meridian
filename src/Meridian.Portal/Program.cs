using Meridian.Infrastructure;
using Meridian.Infrastructure.Persistence;
using Meridian.Portal.Auth;
using Meridian.Portal.Components;
using Meridian.Portal.Ingestion;
using Meridian.Portal.Outreach;
using Meridian.Portal.Sources;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Meridian")
    ?? throw new InvalidOperationException("ConnectionStrings:Meridian is not configured.");

builder.Services.AddMeridianInfrastructure(connectionString, builder.Configuration);
builder.Services.AddDataProtection();

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
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Dev-only: bootstrap schema from the EF model. Production should use
    // explicit migrations rather than EnsureCreated.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
    await db.Database.EnsureCreatedAsync();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantClaimMiddleware>();

app.MapAuthEndpoints();
app.MapWorkspaceEndpoints();
app.MapWebhookIngestEndpoints();
app.MapResendWebhookEndpoints();
app.MapOutboundConfigEndpoints();
app.MapSourceEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
