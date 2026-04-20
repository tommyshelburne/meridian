using FluentAssertions;
using Meridian.Domain.Tenants;

namespace Meridian.Integration;

public class SmokeTest
{
    [Fact]
    public async Task Sqlite_context_can_round_trip_tenant()
    {
        using var fx = new IntegrationTestFixture();
        var tenant = Tenant.Create("Acme", "acme");

        await using (var db = fx.NewDbContext())
        {
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var loaded = await db.Tenants.FindAsync(tenant.Id);
            loaded.Should().NotBeNull();
            loaded!.Slug.Should().Be("acme");
        }
    }
}
