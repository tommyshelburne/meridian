using FluentAssertions;
using Meridian.Application.Auth;

namespace Meridian.Unit.Application.Auth;

public class OidcSchemeNamesTests
{
    [Fact]
    public void Format_TryParse_round_trip()
    {
        var tenantId = Guid.NewGuid();
        var name = OidcSchemeNames.Format(tenantId, "entra-prod");

        OidcSchemeNames.TryParse(name, out var parsedTenant, out var parsedKey).Should().BeTrue();
        parsedTenant.Should().Be(tenantId);
        parsedKey.Should().Be("entra-prod");
    }

    [Fact]
    public void Format_lowercases_provider_key()
    {
        var name = OidcSchemeNames.Format(Guid.NewGuid(), "Entra-Prod");
        name.Should().EndWith(":entra-prod");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cookies")]
    [InlineData("oidc:")]
    [InlineData("oidc:not-a-guid:provider")]
    [InlineData("oidc:00000000-0000-0000-0000-000000000000:")]
    [InlineData("oidc:00000000-0000-0000-0000-000000000000:provider:extra")]
    public void TryParse_returns_false_for_invalid_inputs(string? scheme)
    {
        OidcSchemeNames.TryParse(scheme, out var _, out var _).Should().BeFalse();
    }

    [Fact]
    public void Prefix_is_stable_constant()
    {
        OidcSchemeNames.Prefix.Should().Be("oidc:");
    }
}
