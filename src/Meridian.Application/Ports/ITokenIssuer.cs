using System.Security.Claims;

namespace Meridian.Application.Ports;

public record IssuedToken(string Token, DateTimeOffset ExpiresAt);

public record AccessTokenRequest(
    Guid UserId,
    string Email,
    Guid? TenantId,
    string? TenantSlug,
    string? Role,
    IReadOnlyCollection<Claim>? AdditionalClaims = null);

public interface ITokenIssuer
{
    IssuedToken IssueAccessToken(AccessTokenRequest request);
    IssuedToken IssueRefreshToken(Guid userId);
    ClaimsPrincipal? ValidateAccessToken(string token);
}
