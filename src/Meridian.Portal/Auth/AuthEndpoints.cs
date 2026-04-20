using Meridian.Application.Auth;
using Meridian.Domain.Tenants;
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
public record LoginFormModel(string Email, string Password, string? TotpCode);
public record ForgotPasswordFormModel(string Email);
public record ResetPasswordFormModel(string Token, string Password);
