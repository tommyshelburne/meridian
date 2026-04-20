using FluentAssertions;
using Meridian.Domain.Tenants;

namespace Meridian.Unit.Domain;

public class MarketProfileTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_sets_all_fields()
    {
        var profile = MarketProfile.Create(TenantId, "Federal Contact Centers", "Tier-1 federal agencies");

        profile.Id.Should().NotBeEmpty();
        profile.TenantId.Should().Be(TenantId);
        profile.Name.Should().Be("Federal Contact Centers");
        profile.Description.Should().Be("Tier-1 federal agencies");
        profile.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_requires_tenant()
    {
        var act = () => MarketProfile.Create(Guid.Empty, "Name");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_requires_name()
    {
        var act = () => MarketProfile.Create(TenantId, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivate_and_Activate_toggle_flag()
    {
        var profile = MarketProfile.Create(TenantId, "Test");

        profile.Deactivate();
        profile.IsActive.Should().BeFalse();

        profile.Activate();
        profile.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Rename_updates_name()
    {
        var profile = MarketProfile.Create(TenantId, "Old");

        profile.Rename("New");

        profile.Name.Should().Be("New");
    }
}
