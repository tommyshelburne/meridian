using FluentAssertions;
using Meridian.Domain.Outreach;

namespace Meridian.Unit.Domain.Outreach;

public class OutboundReplyAddressTests
{
    [Fact]
    public void Compose_returns_replies_plus_slug_at_inbound_domain_when_domain_set()
    {
        var composed = OutboundReplyAddress.Compose("reply.meridian.app", "acme", fallback: "anything@x.com");
        composed.Should().Be("replies+acme@reply.meridian.app");
    }

    [Fact]
    public void Compose_falls_back_to_replyto_when_inbound_domain_null()
    {
        var composed = OutboundReplyAddress.Compose(inboundDomain: null, "acme", fallback: "support@acme.example");
        composed.Should().Be("support@acme.example");
    }

    [Fact]
    public void Compose_returns_null_when_neither_inbound_nor_fallback_set()
    {
        var composed = OutboundReplyAddress.Compose(inboundDomain: null, "acme", fallback: null);
        composed.Should().BeNull();
    }

    [Fact]
    public void Compose_falls_back_when_slug_empty_so_routing_cannot_succeed()
    {
        var composed = OutboundReplyAddress.Compose("reply.meridian.app", tenantSlug: "", fallback: "support@acme.example");
        composed.Should().Be("support@acme.example");
    }

    [Fact]
    public void Compose_trims_inbound_domain_whitespace()
    {
        var composed = OutboundReplyAddress.Compose("  reply.meridian.app  ", "acme", fallback: null);
        composed.Should().Be("replies+acme@reply.meridian.app");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-dot-here")]
    [InlineData("contains@at.com")]
    [InlineData("has space.com")]
    [InlineData(".leading.com")]
    [InlineData("trailing.")]
    public void IsValidDomain_rejects_obvious_garbage(string? domain)
    {
        OutboundReplyAddress.IsValidDomain(domain).Should().BeFalse();
    }

    [Fact]
    public void IsValidDomain_rejects_domains_over_253_chars()
    {
        var tooLong = new string('a', 250) + ".com";
        OutboundReplyAddress.IsValidDomain(tooLong).Should().BeFalse();
    }

    [Theory]
    [InlineData("reply.meridian.app")]
    [InlineData("inbound.example.com")]
    [InlineData("a.b")]
    public void IsValidDomain_accepts_reasonable_domains(string domain)
    {
        OutboundReplyAddress.IsValidDomain(domain).Should().BeTrue();
    }
}
