using FluentAssertions;
using Meridian.Domain.Tenants;

namespace Meridian.Unit.Domain;

public class TenantTests
{
    [Fact]
    public void Create_assigns_identity_and_timestamps()
    {
        var tenant = Tenant.Create("Acme Contact Center", "acme");

        tenant.Id.Should().NotBeEmpty();
        tenant.Name.Should().Be("Acme Contact Center");
        tenant.Slug.Should().Be("acme");
        tenant.Plan.Should().Be(PlanTier.Trial);
        tenant.Status.Should().Be(TenantStatus.Trial);
        tenant.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        tenant.UpdatedAt.Should().Be(tenant.CreatedAt);
    }

    [Fact]
    public void Create_with_paid_plan_starts_active()
    {
        var tenant = Tenant.Create("Acme", "acme", PlanTier.Pro);

        tenant.Plan.Should().Be(PlanTier.Pro);
        tenant.Status.Should().Be(TenantStatus.Active);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("has spaces")]
    [InlineData("HasUpper")]
    [InlineData("under_score")]
    public void Create_rejects_invalid_slug(string slug)
    {
        var act = () => Tenant.Create("Acme", slug);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_trims_and_updates_timestamp()
    {
        var tenant = Tenant.Create("Original", "original-co");
        var original = tenant.UpdatedAt;
        Thread.Sleep(5);

        tenant.Rename("  Renamed  ");

        tenant.Name.Should().Be("Renamed");
        tenant.UpdatedAt.Should().BeAfter(original);
    }

    [Fact]
    public void ChangePlan_promotes_trial_to_active()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Status.Should().Be(TenantStatus.Trial);

        tenant.ChangePlan(PlanTier.Starter);

        tenant.Plan.Should().Be(PlanTier.Starter);
        tenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public void Suspend_and_Activate_toggle_status()
    {
        var tenant = Tenant.Create("Acme", "acme", PlanTier.Pro);

        tenant.Suspend();
        tenant.Status.Should().Be(TenantStatus.Suspended);

        tenant.Activate();
        tenant.Status.Should().Be(TenantStatus.Active);
    }
}
