using Meridian.Domain.Users;

namespace Meridian.Application.Auth;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    EmailNotVerified,
    LockedOut,
    Disabled,
    TwoFactorRequired,
    InvalidTotp,
    NoActiveMembership
}

public record LoginTenantMembership(Guid TenantId, string TenantSlug, string TenantName, UserRole Role);

public record LoginResult(
    LoginOutcome Outcome,
    Guid? UserId,
    string? Email,
    string? FullName,
    IReadOnlyList<LoginTenantMembership> Memberships)
{
    public bool IsSuccess => Outcome == LoginOutcome.Success;
    public static LoginResult Fail(LoginOutcome outcome) =>
        new(outcome, null, null, null, Array.Empty<LoginTenantMembership>());
}
