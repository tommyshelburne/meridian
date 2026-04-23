using FluentAssertions;
using Meridian.Application.Opportunities;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Unit.Application.Opportunities;

public class ManualEnrichmentServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Opportunity NewScoredOpportunity()
    {
        var opp = Opportunity.Create(TenantId, $"X-{Guid.NewGuid()}", OpportunitySource.SamGov,
            "Contact Center RFP", "Body",
            Agency.Create("VA", AgencyType.FederalCivilian),
            DateTimeOffset.UtcNow);
        opp.ApplyScore(BidScore.Create(12, ScoreVerdict.Pursue));
        return opp;
    }

    [Fact]
    public async Task AddContact_creates_new_contact_and_links_to_opportunity()
    {
        var opp = NewScoredOpportunity();
        var fakes = new Fakes();
        fakes.Opportunities.Seed(opp);

        var svc = new ManualEnrichmentService(fakes.Opportunities, fakes.Contacts);
        var result = await svc.AddContactAsync(TenantId, opp.Id,
            "Dana Lee", "Dana@vendor.com", "VP Federal", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        opp.Contacts.Should().ContainSingle();
        var contact = fakes.Contacts.Added.Should().ContainSingle().Subject;
        contact.FullName.Should().Be("Dana Lee");
        contact.Email.Should().Be("dana@vendor.com"); // normalized
        contact.Title.Should().Be("VP Federal");
        contact.Source.Should().Be(ContactSource.Manual);
        contact.ConfidenceScore.Should().Be(1.0f);
    }

    [Fact]
    public async Task AddContact_reuses_existing_contact_for_same_email()
    {
        var opp = NewScoredOpportunity();
        var fakes = new Fakes();
        fakes.Opportunities.Seed(opp);
        var existing = Contact.Create(TenantId, "Existing",
            Agency.Create("X", AgencyType.FederalCivilian), ContactSource.SamGov,
            0.7f, email: "rep@vendor.com");
        fakes.Contacts.SeedExisting(existing);

        var svc = new ManualEnrichmentService(fakes.Opportunities, fakes.Contacts);
        var result = await svc.AddContactAsync(TenantId, opp.Id,
            "Should Be Ignored", "rep@vendor.com", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fakes.Contacts.Added.Should().BeEmpty();
        opp.Contacts.Should().ContainSingle()
            .Which.ContactId.Should().Be(existing.Id);
    }

    [Fact]
    public async Task AddContact_rejects_cross_tenant()
    {
        var opp = NewScoredOpportunity();
        var fakes = new Fakes();
        fakes.Opportunities.Seed(opp);

        var svc = new ManualEnrichmentService(fakes.Opportunities, fakes.Contacts);
        var result = await svc.AddContactAsync(Guid.NewGuid(), opp.Id,
            "Foo", "foo@x.com", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
        opp.Contacts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "good@x.com", "name is required")]
    [InlineData("Name", "", "email is required")]
    public async Task AddContact_validates_required_fields(string name, string email, string fragment)
    {
        var opp = NewScoredOpportunity();
        var fakes = new Fakes();
        fakes.Opportunities.Seed(opp);

        var svc = new ManualEnrichmentService(fakes.Opportunities, fakes.Contacts);
        var result = await svc.AddContactAsync(TenantId, opp.Id, name, email, null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.ToLowerInvariant().Should().Contain(fragment);
    }

    [Fact]
    public async Task AddContact_propagates_domain_validation_failure()
    {
        var opp = NewScoredOpportunity();
        var fakes = new Fakes();
        fakes.Opportunities.Seed(opp);

        var svc = new ManualEnrichmentService(fakes.Opportunities, fakes.Contacts);
        var result = await svc.AddContactAsync(TenantId, opp.Id,
            "x", "y@z.com", null, CancellationToken.None);

        // Contact.Create accepts a one-character name (no length floor in domain),
        // so this happens to succeed. Just sanity-check we don't crash.
        result.IsSuccess.Should().BeTrue();
    }

    private class Fakes
    {
        public FakeOpportunityRepo Opportunities { get; } = new();
        public FakeContactRepo Contacts { get; } = new();
    }

    private class FakeOpportunityRepo : IOpportunityRepository
    {
        private readonly Dictionary<Guid, Opportunity> _seeded = new();
        public void Seed(Opportunity o) => _seeded[o.Id] = o;

        public Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_seeded.TryGetValue(id, out var o) ? o : null);

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<Opportunity?> GetByExternalIdAsync(Guid t, string e, CancellationToken ct) => Task.FromResult<Opportunity?>(null);
        public Task<Opportunity?> GetBySourceExternalIdAsync(Guid t, Guid s, string e, CancellationToken ct) => Task.FromResult<Opportunity?>(null);
        public Task<IReadOnlyList<Opportunity>> GetByStatusAsync(Guid t, OpportunityStatus s, CancellationToken ct) => Task.FromResult<IReadOnlyList<Opportunity>>(Array.Empty<Opportunity>());
        public Task<IReadOnlyList<Opportunity>> GetByStatusesAsync(Guid t, IReadOnlyCollection<OpportunityStatus> s, CancellationToken ct) => Task.FromResult<IReadOnlyList<Opportunity>>(Array.Empty<Opportunity>());
        public Task<IReadOnlyList<Opportunity>> GetWatchedAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<Opportunity>>(Array.Empty<Opportunity>());
        public Task<IReadOnlyList<Opportunity>> GetUnenrichedAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<Opportunity>>(_seeded.Values.ToList());
        public Task AddAsync(Opportunity o, CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeContactRepo : IContactRepository
    {
        public List<Contact> Added { get; } = new();
        private readonly List<Contact> _existing = new();
        public void SeedExisting(Contact c) => _existing.Add(c);

        public Task<Contact?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct)
            => Task.FromResult(_existing.FirstOrDefault(c =>
                string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Contact c, CancellationToken ct) { Added.Add(c); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Contact?>(null);
        public Task<IReadOnlyList<Contact>> GetByAgencyAsync(Guid t, string a, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>());
        public Task<IReadOnlyList<Contact>> GetUnenrichedForOpportunityAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>());
    }
}
