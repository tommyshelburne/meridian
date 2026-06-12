using FluentAssertions;
using Meridian.Application.Demo;
using Meridian.Domain.Common;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Persistence.Repositories;
using Meridian.Infrastructure.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Integration;

public class DemoTenantServiceTests
{
    private const string Slug = "demo-acme";
    private const string Email = "demo@meridianbd.dev";
    private const string Password = "demo-walkthrough-2026";

    [Fact]
    public async Task Provision_creates_tenant_verified_owner_and_console_outbound()
    {
        using var fx = new IntegrationTestFixture();

        await using (var db = fx.NewDbContext())
        {
            var result = await BuildService(fx, db).ProvisionAsync(
                new DemoProvisionRequest(Slug, "Acme Demo", Email, "Demo Operator", Password),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.AlreadyExisted.Should().BeFalse();
        }

        await using (var db = fx.NewDbContext())
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Slug == Slug);
            tenant.Status.Should().Be(TenantStatus.Active);

            var user = await db.Users.SingleAsync(u => u.Email == Email);
            user.EmailVerified.Should().BeTrue("demo operator must log in without a verification round-trip");

            var membership = await db.UserTenants.SingleAsync(m => m.UserId == user.Id && m.TenantId == tenant.Id);
            membership.Role.Should().Be(Meridian.Domain.Users.UserRole.Owner);

            var outbound = await db.OutboundConfigurations.IgnoreQueryFilters()
                .SingleAsync(c => c.TenantId == tenant.Id);
            outbound.ProviderType.Should().Be(OutboundProviderType.Console,
                "the demo tenant must be sandboxed — sequences run but nothing real ever sends");
        }
    }

    [Fact]
    public async Task Provision_seeds_opportunities_contacts_sequences_and_history()
    {
        using var fx = new IntegrationTestFixture();
        var tenantId = await ProvisionAsync(fx);

        fx.TenantContext.SetTenant(tenantId);
        await using var db = fx.NewDbContext();

        var opportunities = await db.Opportunities.ToListAsync();
        opportunities.Should().HaveCountGreaterThanOrEqualTo(8);
        opportunities.Select(o => o.Status).Distinct().Should().HaveCountGreaterThanOrEqualTo(4,
            "the pipeline board needs cards across several columns out of the box");
        opportunities.Count(o => o.Status == OpportunityStatus.New).Should().Be(1,
            "exactly one fresh opportunity is left for the live-ingestion demo beat");

        (await db.Contacts.CountAsync()).Should().BeGreaterThanOrEqualTo(5);
        (await db.OutreachTemplates.CountAsync()).Should().BeGreaterThanOrEqualTo(2);

        var sequences = await db.OutreachSequences.Include(s => s.Steps).ToListAsync();
        sequences.Should().HaveCountGreaterThanOrEqualTo(2);
        sequences.Should().OnlyContain(s => s.Steps.Count > 0);

        var enrollments = await db.OutreachEnrollments.ToListAsync();
        enrollments.Should().HaveCountGreaterThanOrEqualTo(3);
        enrollments.Select(e => e.Status).Distinct().Should().HaveCountGreaterThanOrEqualTo(2);

        var activities = await db.EmailActivities.ToListAsync();
        activities.Should().HaveCountGreaterThanOrEqualTo(4);
        activities.Should().Contain(a => a.Status == EmailStatus.Replied,
            "the replies view needs at least one reply to show");
    }

    [Fact]
    public async Task Provision_is_idempotent_second_call_adds_nothing()
    {
        using var fx = new IntegrationTestFixture();
        var tenantId = await ProvisionAsync(fx);
        var before = await SnapshotCountsAsync(fx, tenantId);

        await using (var db = fx.NewDbContext())
        {
            var second = await BuildService(fx, db).ProvisionAsync(
                new DemoProvisionRequest(Slug, "Acme Demo", Email, "Demo Operator", Password),
                CancellationToken.None);
            second.IsSuccess.Should().BeTrue(second.Error);
            second.Value!.AlreadyExisted.Should().BeTrue();
        }

        var after = await SnapshotCountsAsync(fx, tenantId);
        after.Should().Be(before);
    }

    [Fact]
    public async Task Provision_rejects_non_demo_slug()
    {
        using var fx = new IntegrationTestFixture();

        await using var db = fx.NewDbContext();
        var result = await BuildService(fx, db).ProvisionAsync(
            new DemoProvisionRequest("acme-corp", "Acme", Email, "Demo Operator", Password),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        (await db.Tenants.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Demo_outbound_uses_reserved_addresses_and_console_provider()
    {
        using var fx = new IntegrationTestFixture();
        var tenantId = await ProvisionAsync(fx);

        fx.TenantContext.SetTenant(tenantId);
        await using var db = fx.NewDbContext();

        var outbound = await db.OutboundConfigurations.SingleAsync();
        outbound.ProviderType.Should().Be(OutboundProviderType.Console);

        // Belt and braces: even if the provider were ever flipped, no seeded
        // contact may carry a deliverable address (RFC 2606 reserved domains only).
        var contacts = await db.Contacts.ToListAsync();
        contacts.Should().OnlyContain(c =>
            c.Email == null || c.Email.EndsWith("@example.com") ||
            c.Email.EndsWith("@example.org") || c.Email.EndsWith("@example.net"));
    }

    [Fact]
    public async Task Reset_restores_pristine_seeded_state_after_mutations()
    {
        using var fx = new IntegrationTestFixture();
        var tenantId = await ProvisionAsync(fx);
        var pristine = await SnapshotCountsAsync(fx, tenantId);

        // Mutate the tenant the way a live demo would: decide an opportunity,
        // add a contact, opt one out.
        fx.TenantContext.SetTenant(tenantId);
        await using (var db = fx.NewDbContext())
        {
            var fresh = await db.Opportunities.SingleAsync(o => o.Status == OpportunityStatus.New);
            fresh.Pursue();
            db.Contacts.Add(Meridian.Domain.Contacts.Contact.Create(
                tenantId, "Walk-in Prospect", Agency.Create("Somewhere City", AgencyType.StateLocal),
                ContactSource.Manual, 0.9f, email: "walkin@example.com"));
            var optOut = await db.Contacts.FirstAsync();
            optOut.OptOut();
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var reset = await BuildService(fx, db).ResetAsync(Slug, CancellationToken.None);
            reset.IsSuccess.Should().BeTrue(reset.Error);
        }

        var restored = await SnapshotCountsAsync(fx, tenantId);
        restored.Should().Be(pristine);

        fx.TenantContext.SetTenant(tenantId);
        await using (var db2 = fx.NewDbContext())
        {
            (await db2.Opportunities.CountAsync(o => o.Status == OpportunityStatus.New)).Should().Be(1);
            (await db2.Contacts.AnyAsync(c => c.FullName == "Walk-in Prospect")).Should().BeFalse();
            (await db2.Contacts.AnyAsync(c => c.IsOptedOut)).Should().BeFalse();

            // The operator login survives reset — only demo data is recycled.
            var tenant = await db2.Tenants.SingleAsync(t => t.Slug == Slug);
            var user = await db2.Users.SingleAsync(u => u.Email == Email);
            (await db2.UserTenants.AnyAsync(m => m.UserId == user.Id && m.TenantId == tenant.Id)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Reset_rejects_non_demo_slug()
    {
        using var fx = new IntegrationTestFixture();

        await using (var db = fx.NewDbContext())
        {
            db.Tenants.Add(Tenant.Create("Real Customer", "realco"));
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var reset = await BuildService(fx, db).ResetAsync("realco", CancellationToken.None);
            reset.IsSuccess.Should().BeFalse("reset must never be able to touch a real tenant");
        }
    }

    [Fact]
    public async Task Seed_does_not_leak_across_tenants()
    {
        using var fx = new IntegrationTestFixture();
        var tenantA = await ProvisionAsync(fx, "demo-alpha", "alpha-demo@meridianbd.dev");
        var tenantB = await ProvisionAsync(fx, "demo-beta", "beta-demo@meridianbd.dev");

        var countsB = await SnapshotCountsAsync(fx, tenantB);

        // Mutate then reset tenant A; tenant B must be untouched.
        fx.TenantContext.SetTenant(tenantA);
        await using (var db = fx.NewDbContext())
        {
            (await db.Opportunities.FirstAsync(o => o.Status == OpportunityStatus.New)).Pursue();
            await db.SaveChangesAsync();
        }
        await using (var db = fx.NewDbContext())
        {
            var reset = await BuildService(fx, db).ResetAsync("demo-alpha", CancellationToken.None);
            reset.IsSuccess.Should().BeTrue(reset.Error);
        }

        tenantA.Should().NotBe(tenantB);
        (await SnapshotCountsAsync(fx, tenantB)).Should().Be(countsB);
    }

    private static DemoTenantService BuildService(IntegrationTestFixture fx, MeridianDbContext db)
    {
        var scoringConfig = new ScoringConfiguration();
        var seed = new DemoSeedService(
            new OpportunityRepository(db),
            new ContactRepository(db),
            new OutreachRepository(db),
            new OutboundConfigurationRepository(db),
            new BidScoringEngine(scoringConfig, new SeatCountEstimator(scoringConfig)));
        return new DemoTenantService(
            new TenantRepository(db),
            new UserRepository(db),
            new UserTenantRepository(db),
            fx.Passwords,
            fx.TenantContext,
            seed,
            new DemoDataWiper(db));
    }

    private static async Task<Guid> ProvisionAsync(
        IntegrationTestFixture fx, string slug = Slug, string email = Email)
    {
        await using var db = fx.NewDbContext();
        var result = await BuildService(fx, db).ProvisionAsync(
            new DemoProvisionRequest(slug, "Demo Tenant", email, "Demo Operator", Password),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!.TenantId;
    }

    // One comparable value covering every seeded table, so pristine-state
    // assertions can't silently miss a collection.
    private static async Task<string> SnapshotCountsAsync(IntegrationTestFixture fx, Guid tenantId)
    {
        fx.TenantContext.SetTenant(tenantId);
        await using var db = fx.NewDbContext();
        var opportunities = await db.Opportunities.OrderBy(o => o.ExternalId)
            .Select(o => o.ExternalId + ":" + o.Status).ToListAsync();
        var counts = new[]
        {
            await db.Contacts.CountAsync(),
            await db.OutreachTemplates.CountAsync(),
            await db.OutreachSequences.CountAsync(),
            await db.OutreachEnrollments.CountAsync(),
            await db.EmailActivities.CountAsync(),
            await db.OutboundConfigurations.CountAsync(),
        };
        return string.Join("|", opportunities) + "##" + string.Join(",", counts);
    }
}
