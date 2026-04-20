using System.Security.Claims;
using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure.Auth;

public class JwtTokenIssuerTests
{
    private static JwtTokenIssuer Create(int accessMinutes = 60)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "meridian-test",
            Audience = "portal-test",
            SigningKey = "test-signing-key-that-is-long-enough-for-hmac-256",
            AccessTokenLifetimeMinutes = accessMinutes
        });
        return new JwtTokenIssuer(options);
    }

    [Fact]
    public void IssueAccessToken_roundtrips_through_validation()
    {
        var issuer = Create();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var issued = issuer.IssueAccessToken(new AccessTokenRequest(
            userId, "user@example.com", tenantId, "acme", "Owner"));

        issued.Token.Should().NotBeNullOrWhiteSpace();
        issued.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        var principal = issuer.ValidateAccessToken(issued.Token);
        principal.Should().NotBeNull();
        principal!.FindFirst("sub")!.Value.Should().Be(userId.ToString());
        principal.FindFirst("tenant_id")!.Value.Should().Be(tenantId.ToString());
        principal.FindFirst("tenant_slug")!.Value.Should().Be("acme");
        principal.FindFirst(ClaimTypes.Role)!.Value.Should().Be("Owner");
    }

    [Fact]
    public void ValidateAccessToken_rejects_tampered_token()
    {
        var issuer = Create();
        var issued = issuer.IssueAccessToken(new AccessTokenRequest(
            Guid.NewGuid(), "user@example.com", null, null, null));

        var tampered = issued.Token + "x";
        issuer.ValidateAccessToken(tampered).Should().BeNull();
    }

    [Fact]
    public void Constructor_rejects_short_signing_key()
    {
        var bad = Options.Create(new JwtOptions { SigningKey = "short" });
        var act = () => new JwtTokenIssuer(bad);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IssueRefreshToken_contains_user_id_prefix_and_expires_in_future()
    {
        var issuer = Create();
        var userId = Guid.NewGuid();

        var refresh = issuer.IssueRefreshToken(userId);

        refresh.Token.Should().StartWith(userId.ToString("N"));
        refresh.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(13));
    }
}
