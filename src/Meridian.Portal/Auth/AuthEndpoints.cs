using Meridian.Application.Auth;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Meridian.Portal.Auth.Oidc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").DisableAntiforgery();

        group.MapPost("/signup", async (
            [FromForm] SignupFormModel form,
            AuthService auth,
            CancellationToken ct) =>
        {
            var slug = (form.TenantSlug ?? string.Empty).Trim().ToLowerInvariant();
            var result = await auth.SignupAsync(
                new SignupRequest(form.Email, form.FullName, form.Password, form.TenantName, slug),
                ct);

            return result.IsSuccess
                ? Results.Redirect($"/verify-email-sent?email={Uri.EscapeDataString(form.Email)}")
                : Results.Redirect($"/signup?error={Uri.EscapeDataString(result.Error!)}");
        });

        group.MapPost("/login", async (
            HttpContext http,
            [FromForm] LoginFormModel form,
            AuthService auth,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(
                new LoginRequest(form.Email, form.Password, form.TotpCode),
                ct);

            if (!result.IsSuccess)
                return RedirectForLoginOutcome(result.Outcome, form.Email);

            var selected = result.Memberships[0];
            tenantContext.SetTenant(selected.TenantId);
            var principal = ClaimsBuilder.Build(result, selected);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Results.Redirect($"/app/{selected.TenantSlug}");
        });

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });

        group.MapPost("/forgot-password", async (
            [FromForm] ForgotPasswordFormModel form,
            AuthService auth,
            CancellationToken ct) =>
        {
            await auth.RequestPasswordResetAsync(form.Email, ct);
            return Results.Redirect("/forgot-password?sent=1");
        });

        group.MapPost("/reset-password", async (
            [FromForm] ResetPasswordFormModel form,
            AuthService auth,
            CancellationToken ct) =>
        {
            var result = await auth.ResetPasswordAsync(form.Token, form.Password, ct);
            return result.IsSuccess
                ? Results.Redirect("/login?reset=1")
                : Results.Redirect($"/reset-password?token={Uri.EscapeDataString(form.Token)}&error={Uri.EscapeDataString(result.Error!)}");
        });

        // OIDC challenge: /auth/oidc/{providerKey}/challenge?tenant={slug}.
        // Resolves the tenant by slug, looks up the OidcConfig for that tenant +
        // providerKey, and triggers an OIDC challenge against the dynamically-
        // manufactured scheme. The OIDC handler's CallbackPath is set per-options to
        // /auth/oidc/{providerKey}/callback — that path is intercepted by the handler
        // itself before it reaches any endpoint, so no callback endpoint is mapped here.
        group.MapGet("/oidc/{providerKey}/challenge", async (
            string providerKey,
            [FromQuery] string? tenant,
            HttpContext http,
            ITenantRepository tenants,
            IOidcConfigRepository configs,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(tenant))
                return Results.Redirect("/login?error=missing-tenant");

            var resolved = await tenants.GetBySlugAsync(tenant, ct);
            if (resolved is null)
                return Results.Redirect("/login?error=unknown-tenant");

            var config = await configs.GetByProviderKeyAsync(resolved.Id, providerKey, ct);
            if (config is null || !config.IsEnabled)
                return Results.Redirect($"/login?tenant={Uri.EscapeDataString(tenant)}&error=sso-not-configured");

            var schemeName = OidcSchemeNames.Format(resolved.Id, providerKey);
            var props = new AuthenticationProperties { RedirectUri = $"/app/{resolved.Slug}" };
            return Results.Challenge(props, new[] { schemeName });
        });

        return app;
    }

    private static IResult RedirectForLoginOutcome(LoginOutcome outcome, string email)
    {
        var query = $"email={Uri.EscapeDataString(email)}";
        return outcome switch
        {
            LoginOutcome.TwoFactorRequired => Results.Redirect($"/login?{query}&twofactor=1"),
            LoginOutcome.InvalidTotp => Results.Redirect($"/login?{query}&twofactor=1&error=invalid-code"),
            LoginOutcome.EmailNotVerified => Results.Redirect($"/login?{query}&error=unverified"),
            LoginOutcome.LockedOut => Results.Redirect($"/login?{query}&error=locked"),
            LoginOutcome.Disabled => Results.Redirect($"/login?{query}&error=disabled"),
            LoginOutcome.NoActiveMembership => Results.Redirect($"/login?{query}&error=no-workspace"),
            _ => Results.Redirect($"/login?{query}&error=invalid")
        };
    }
}

public record SignupFormModel(string Email, string FullName, string Password, string TenantName, string TenantSlug);
public class LoginFormModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? TotpCode { get; set; }
}
public record ForgotPasswordFormModel(string Email);
public record ResetPasswordFormModel(string Token, string Password);
