using System.Security.Claims;
using Meridian.Application.Auth;
using Meridian.Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Portal.Auth;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/app/{slug}").RequireAuthorization().DisableAntiforgery();

        group.MapPost("/settings/rename", async (
            string slug,
            HttpContext http,
            [FromForm] RenameTenantForm form,
            TenantManagementService mgmt,
            CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Results.Redirect($"/app/{slug}/settings?error={Uri.EscapeDataString("Session expired.")}");
            var result = await mgmt.RenameTenantAsync(tenantId, form.Name, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/settings?saved=1")
                : Results.Redirect($"/app/{slug}/settings?error={Uri.EscapeDataString(result.Error!)}");
        });

        group.MapPost("/members/invite", async (
            string slug,
            HttpContext http,
            [FromForm] InviteMemberForm form,
            MembershipService memberships,
            CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId) ||
                !TryResolveUserId(http, out var inviterId))
                return Results.Redirect($"/app/{slug}/members?error={Uri.EscapeDataString("Session expired.")}");
            if (!Enum.TryParse<UserRole>(form.Role, out var role) || role == UserRole.Owner)
                return Results.Redirect($"/app/{slug}/members?error={Uri.EscapeDataString("Invalid role.")}");
            var result = await memberships.InviteAsync(tenantId, form.Email, form.FullName, role, inviterId, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/members?invited=1")
                : Results.Redirect($"/app/{slug}/members?error={Uri.EscapeDataString(result.Error!)}");
        });

        group.MapPost("/members/remove", async (
            string slug,
            HttpContext http,
            [FromForm] RemoveMemberForm form,
            MembershipService memberships,
            CancellationToken ct) =>
        {
            if (!TryResolveTenantId(http, out var tenantId))
                return Results.Redirect($"/app/{slug}/members?error={Uri.EscapeDataString("Session expired.")}");
            if (!Guid.TryParse(form.UserId, out var targetUserId))
                return Results.Redirect($"/app/{slug}/members?error={Uri.EscapeDataString("Invalid user.")}");
            var result = await memberships.RemoveAsync(tenantId, targetUserId, ct);
            return result.IsSuccess
                ? Results.Redirect($"/app/{slug}/members")
                : Results.Redirect($"/app/{slug}/members?error={Uri.EscapeDataString(result.Error!)}");
        });

        return app;
    }

    private static bool TryResolveTenantId(HttpContext http, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var claim = http.User.FindFirst(ClaimsBuilder.TenantIdClaim)?.Value;
        return Guid.TryParse(claim, out tenantId);
    }

    private static bool TryResolveUserId(HttpContext http, out Guid userId)
    {
        userId = Guid.Empty;
        var claim = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out userId);
    }
}

public record RenameTenantForm(string Name);
public record InviteMemberForm(string Email, string FullName, string Role);
public record RemoveMemberForm(string UserId);
