using FluentAssertions;
using Meridian.Domain.Outreach;

namespace Meridian.Unit.Domain.Outreach;

public class OutboundConfigurationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static OutboundConfiguration Valid(
        OutboundProviderType type = OutboundProviderType.Resend,
        string apiKey = "encrypted-key",
        string fromAddress = "outreach@vendor.com",
        string fromName = "Vendor",
        string physicalAddress = "1 Main St, City, ST 00000",
        string unsubscribeBaseUrl = "https://example.com/unsubscribe")
        => OutboundConfiguration.Create(TenantId, type, apiKey, fromAddress, fromName,
            physicalAddress, unsubscribeBaseUrl);

    [Fact]
    public void Create_persists_required_fields_and_enables_by_default()
    {
        var config = Valid();
        config.TenantId.Should().Be(TenantId);
        config.ProviderType.Should().Be(OutboundProviderType.Resend);
        config.IsEnabled.Should().BeTrue();
        config.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Console_provider_does_not_require_api_key()
    {
        var config = Valid(type: OutboundProviderType.Console, apiKey: "");
        config.EncryptedApiKey.Should().BeEmpty();
    }

    [Fact]
    public void Resend_provider_requires_api_key()
    {
        var act = () => Valid(type: OutboundProviderType.Resend, apiKey: "");
        act.Should().Throw<ArgumentException>().WithMessage("*API key*");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@x.com")]
    [InlineData("missing-at-sign.com")]
    [InlineData("")]
    public void Invalid_from_address_is_rejected(string address)
    {
        var act = () => Valid(fromAddress: address);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/u")]
    [InlineData("javascript:alert(1)")]
    public void Unsubscribe_url_must_be_absolute_http_or_https(string url)
    {
        var act = () => Valid(unsubscribeBaseUrl: url);
        act.Should().Throw<ArgumentException>().WithMessage("*UnsubscribeBaseUrl*");
    }

    [Fact]
    public void Physical_address_required_for_can_spam()
    {
        var act = () => Valid(physicalAddress: "");
        act.Should().Throw<ArgumentException>().WithMessage("*CAN-SPAM*");
    }

    [Fact]
    public void UpdateProvider_swaps_type_and_key()
    {
        var config = Valid();
        config.UpdateProvider(OutboundProviderType.Console, string.Empty);

        config.ProviderType.Should().Be(OutboundProviderType.Console);
        config.EncryptedApiKey.Should().BeEmpty();
    }

    [Fact]
    public void UpdateSender_validates_new_addresses()
    {
        var config = Valid();
        var act = () => config.UpdateSender("garbage", "Name");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Disable_then_enable_toggles_flag()
    {
        var config = Valid();
        config.Disable();
        config.IsEnabled.Should().BeFalse();
        config.Enable();
        config.IsEnabled.Should().BeTrue();
    }
}
