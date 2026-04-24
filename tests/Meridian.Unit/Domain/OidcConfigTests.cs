using FluentAssertions;
using Meridian.Domain.Auth;

namespace Meridian.Unit.Domain;

public class OidcConfigTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_sets_fields_and_defaults()
    {
        var config = OidcConfig.Create(TenantId, "ENTRA-Prod", OidcProvider.EntraId,
            "Acme SSO", "https://login.microsoftonline.com/tenant-id/v2.0/",
            "client-id", "client-secret");

        config.TenantId.Should().Be(TenantId);
        config.ProviderKey.Should().Be("entra-prod");
        config.Provider.Should().Be(OidcProvider.EntraId);
        config.DisplayName.Should().Be("Acme SSO");
        config.Authority.Should().Be("https://login.microsoftonline.com/tenant-id/v2.0");
        config.Scopes.Should().Be(OidcConfig.DefaultScopes);
        config.EmailClaim.Should().Be("email");
        config.NameClaim.Should().Be("name");
        config.IsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("")]
    public void Create_rejects_invalid_authority(string authority)
    {
        var act = () => OidcConfig.Create(TenantId, "key", OidcProvider.Generic,
            "Display", authority, "client", "secret");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_tenant()
    {
        var act = () => OidcConfig.Create(Guid.Empty, "key", OidcProvider.Generic,
            "Display", "https://example.com", "client", "secret");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RotateSecret_updates_secret_and_timestamp()
    {
        var config = OidcConfig.Create(TenantId, "key", OidcProvider.Generic,
            "Display", "https://example.com", "client", "secret1");
        var originalUpdated = config.UpdatedAt;
        Thread.Sleep(5);

        config.RotateSecret("secret2");

        config.EncryptedClientSecret.Should().Be("secret2");
        config.UpdatedAt.Should().BeAfter(originalUpdated);
    }

    [Fact]
    public void Enable_and_Disable_toggle_flag()
    {
        var config = OidcConfig.Create(TenantId, "key", OidcProvider.Generic,
            "Display", "https://example.com", "client", "secret");

        config.Disable();
        config.IsEnabled.Should().BeFalse();

        config.Enable();
        config.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void UpdateDetails_applies_new_values()
    {
        var config = OidcConfig.Create(TenantId, "key", OidcProvider.Generic,
            "Display", "https://example.com", "client", "secret");

        config.UpdateDetails("New Name", "https://new.example.com", "new-client",
            "openid profile email offline_access", "upn", "given_name");

        config.DisplayName.Should().Be("New Name");
        config.Authority.Should().Be("https://new.example.com");
        config.ClientId.Should().Be("new-client");
        config.Scopes.Should().Be("openid profile email offline_access");
        config.EmailClaim.Should().Be("upn");
        config.NameClaim.Should().Be("given_name");
    }
}
