using FluentAssertions;
using Meridian.Domain.Sources;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence.Repositories;

namespace Meridian.Integration;

public class SourceDefinitionPersistenceTests
{
    [Fact]
    public async Task Round_trips_source_and_isolates_by_tenant()
    {
        using var fx = new IntegrationTestFixture();
        var tenantA = Tenant.Create("Acme", "acme");
        var tenantB = Tenant.Create("Beta", "beta");

        await using (var db = fx.NewDbContext())
        {
            db.Tenants.AddRange(tenantA, tenantB);
            db.SourceDefinitions.Add(SourceDefinition.Create(
                tenantA.Id, SourceAdapterType.SamGov, "Acme SAM", "{\"keywords\":[\"cloud\"]}"));
            db.SourceDefinitions.Add(SourceDefinition.Create(
                tenantB.Id, SourceAdapterType.GenericRss, "Beta RSS", "{}"));
            await db.SaveChangesAsync();
        }

        fx.TenantContext.SetTenant(tenantA.Id);
        await using (var db = fx.NewDbContext())
        {
            var repo = new SourceDefinitionRepository(db);
            var tenantAOwned = await repo.GetForTenantAsync(tenantA.Id, CancellationToken.None);
            tenantAOwned.Should().ContainSingle().Which.Name.Should().Be("Acme SAM");
        }

        fx.TenantContext.SetTenant(tenantB.Id);
        await using (var db = fx.NewDbContext())
        {
            var repo = new SourceDefinitionRepository(db);
            var tenantBOwned = await repo.GetForTenantAsync(tenantB.Id, CancellationToken.None);
            tenantBOwned.Should().ContainSingle().Which.AdapterType.Should().Be(SourceAdapterType.GenericRss);
        }
    }

    [Fact]
    public async Task GetAllEnabledAcrossTenantsAsync_returns_all_tenants_for_worker()
    {
        using var fx = new IntegrationTestFixture();
        var tenantA = Tenant.Create("Acme", "acme");
        var tenantB = Tenant.Create("Beta", "beta");
        var disabled = SourceDefinition.Create(tenantA.Id, SourceAdapterType.SamGov, "Disabled", "{}");
        disabled.Disable();

        await using (var db = fx.NewDbContext())
        {
            db.Tenants.AddRange(tenantA, tenantB);
            db.SourceDefinitions.AddRange(
                SourceDefinition.Create(tenantA.Id, SourceAdapterType.SamGov, "Acme 1", "{}"),
                SourceDefinition.Create(tenantB.Id, SourceAdapterType.GenericRss, "Beta 1", "{}"),
                disabled);
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var repo = new SourceDefinitionRepository(db);
            var all = await repo.GetAllEnabledAcrossTenantsAsync(CancellationToken.None);
            all.Should().HaveCount(2);
            all.Select(s => s.Name).Should().BeEquivalentTo(new[] { "Acme 1", "Beta 1" });
        }
    }

    [Fact]
    public async Task State_transitions_persist_across_sessions()
    {
        using var fx = new IntegrationTestFixture();
        var tenant = Tenant.Create("Acme", "acme");
        var source = SourceDefinition.Create(tenant.Id, SourceAdapterType.SamGov, "SAM", "{}");

        await using (var db = fx.NewDbContext())
        {
            db.Tenants.Add(tenant);
            db.SourceDefinitions.Add(source);
            await db.SaveChangesAsync();
        }

        fx.TenantContext.SetTenant(tenant.Id);

        await using (var db = fx.NewDbContext())
        {
            var loaded = await db.SourceDefinitions.FindAsync(source.Id);
            loaded!.MarkRunStarted();
            loaded.MarkRunFailed("boom");
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var loaded = await db.SourceDefinitions.FindAsync(source.Id);
            loaded!.LastRunStatus.Should().Be(SourceRunStatus.Failed);
            loaded.LastRunError.Should().Be("boom");
            loaded.ConsecutiveFailures.Should().Be(1);
        }
    }
}
