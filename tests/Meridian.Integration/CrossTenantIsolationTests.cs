using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Tenants;

namespace Meridian.Integration;

public class CrossTenantIsolationTests
{
    [Fact]
    public async Task Query_filter_hides_opportunities_from_other_tenants()
    {
        using var fx = new IntegrationTestFixture();
        var tenantA = Tenant.Create("Acme", "acme");
        var tenantB = Tenant.Create("Beta", "beta");

        // Seed both tenants with one opportunity each — bypass filter with a no-tenant context.
        await using (var db = fx.NewDbContext())
        {
            db.Tenants.AddRange(tenantA, tenantB);
            db.Opportunities.Add(BuildOpportunity(tenantA.Id, "sam-A-1", "Acme opp"));
            db.Opportunities.Add(BuildOpportunity(tenantB.Id, "sam-B-1", "Beta opp"));
            await db.SaveChangesAsync();
        }

        fx.TenantContext.SetTenant(tenantA.Id);
        await using (var db = fx.NewDbContext())
        {
            var opps = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(db.Opportunities);
            opps.Should().HaveCount(1);
            opps[0].Title.Should().Be("Acme opp");
        }

        fx.TenantContext.SetTenant(tenantB.Id);
        await using (var db = fx.NewDbContext())
        {
            var opps = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(db.Opportunities);
            opps.Should().HaveCount(1);
            opps[0].Title.Should().Be("Beta opp");
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_returns_all_tenants_data()
    {
        using var fx = new IntegrationTestFixture();
        var tenantA = Tenant.Create("Acme", "acme");
        var tenantB = Tenant.Create("Beta", "beta");

        await using (var db = fx.NewDbContext())
        {
            db.Tenants.AddRange(tenantA, tenantB);
            db.Opportunities.Add(BuildOpportunity(tenantA.Id, "sam-A-1", "A"));
            db.Opportunities.Add(BuildOpportunity(tenantB.Id, "sam-B-1", "B"));
            await db.SaveChangesAsync();
        }

        fx.TenantContext.SetTenant(tenantA.Id);
        await using (var ctx = fx.NewDbContext())
        {
            var all = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .IgnoreQueryFilters(ctx.Opportunities));
            all.Should().HaveCount(2);
        }
    }

    private static Opportunity BuildOpportunity(Guid tenantId, string externalId, string title) =>
        Opportunity.Create(
            tenantId,
            externalId,
            OpportunitySource.SamGov,
            title,
            "Desc",
            Agency.Create("Test Agency", AgencyType.FederalCivilian),
            DateTimeOffset.UtcNow);
}
