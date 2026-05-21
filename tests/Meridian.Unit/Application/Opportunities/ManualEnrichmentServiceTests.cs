using FluentAssertions;
using Meridian.Application.Opportunities;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;
using Meridian.Domain.Scoring;
using Microsoft.Extensions.Logging.Abstractions;

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

        var result = await fakes.CreateService().AddContactAsync(TenantId, opp.Id,
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

        var result = await fakes.CreateService().AddContactAsync(TenantId, opp.Id,
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

        var result = await fakes.CreateService().AddContactAsync(Guid.NewGuid(), opp.Id,
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

        var result = await fakes.CreateService().AddContactAsync(TenantId, opp.Id, name, email, null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.ToLowerInvariant().Should().Contain(fragment);
    }

    [Fact]
    public async Task AddContact_succeeds_for_a_minimal_valid_contact()
    {
        var opp = NewScoredOpportunity();
        var fakes = new Fakes();
        fakes.Opportunities.Seed(opp);

        var result = await fakes.CreateService().AddContactAsync(TenantId, opp.Id,
            "x", "y@z.com", null, CancellationToken.None);

        // Contact.Create accepts a one-character name (no length floor in domain).
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AddContact_enrolls_the_contact_when_a_sequence_is_configured()
    {
        var opp = NewScoredOpportunity();
        var fakes = new Fakes();
        fakes.Opportunities.Seed(opp);

        var template = OutreachTemplate.Create(TenantId, "Initial", "Re: {{opportunity.title}}",
            "Hi {{contact.first_name}}");
        var sequence = OutreachSequence.Create(TenantId, "MVP", OpportunityType.Rfp, AgencyType.FederalCivilian);
        sequence.AddStep(0, template.Id, "Re: {{opportunity.title}}", TimeSpan.Zero, TimeSpan.FromHours(23.99));
        fakes.Outreach.SeedTemplate(template);
        fakes.Outreach.SeedSequence(sequence);

        var result = await fakes.CreateService().AddContactAsync(TenantId, opp.Id,
            "Dana Lee", "dana@vendor.com", "VP Federal", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var enrollment = fakes.Outreach.Enrollments.Should().ContainSingle().Subject;
        enrollment.OpportunityId.Should().Be(opp.Id);
        enrollment.SequenceId.Should().Be(sequence.Id);
        enrollment.Status.Should().Be(EnrollmentStatus.Active);
        enrollment.NextSendAt.Should().NotBeNull("the next SequenceJob run must have a send queued");
        fakes.Audit.Events.Should().ContainSingle(e => e.EventType == "EnrollmentCreated");
    }

    [Fact]
    public async Task AddContact_attaches_but_does_not_enroll_when_no_sequence_configured()
    {
        var opp = NewScoredOpportunity();
        var fakes = new Fakes();
        fakes.Opportunities.Seed(opp);

        var result = await fakes.CreateService().AddContactAsync(TenantId, opp.Id,
            "Dana Lee", "dana@vendor.com", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        opp.Contacts.Should().ContainSingle("the contact is still attached");
        fakes.Outreach.Enrollments.Should().BeEmpty("no sequence configured means no enrollment");
    }

    private class Fakes
    {
        public FakeOpportunityRepo Opportunities { get; } = new();
        public FakeContactRepo Contacts { get; } = new();
        public FakeOutreachRepo Outreach { get; } = new();
        public FakeAuditLog Audit { get; } = new();

        public ManualEnrichmentService CreateService()
        {
            var enrollment = new OutreachEnrollmentService(
                Outreach, Contacts, Audit, NullLogger<OutreachEnrollmentService>.Instance);
            return new ManualEnrichmentService(Opportunities, Contacts, Outreach, enrollment);
        }
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
            => Task.FromResult(Added.Concat(_existing).FirstOrDefault(c =>
                string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Added.Concat(_existing).FirstOrDefault(c => c.Id == id));

        public Task AddAsync(Contact c, CancellationToken ct) { Added.Add(c); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Contact>> GetByAgencyAsync(Guid t, string a, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>());
        public Task<IReadOnlyList<Contact>> GetUnenrichedForOpportunityAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>());
    }

    private class FakeOutreachRepo : IOutreachRepository
    {
        private readonly List<OutreachSequence> _sequences = new();
        private readonly List<OutreachTemplate> _templates = new();
        public List<OutreachEnrollment> Enrollments { get; } = new();
        public List<SequenceSnapshot> Snapshots { get; } = new();

        public void SeedSequence(OutreachSequence s) => _sequences.Add(s);
        public void SeedTemplate(OutreachTemplate t) => _templates.Add(t);

        public Task<IReadOnlyList<OutreachSequence>> GetSequencesAsync(Guid t, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<OutreachSequence>>(_sequences.ToList());
        public Task<OutreachTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_templates.FirstOrDefault(x => x.Id == id));
        public Task<OutreachEnrollment?> GetEnrollmentAsync(Guid t, Guid c, Guid o, CancellationToken ct)
            => Task.FromResult(Enrollments.FirstOrDefault(e => e.ContactId == c && e.OpportunityId == o));
        public Task AddEnrollmentAsync(OutreachEnrollment e, CancellationToken ct) { Enrollments.Add(e); return Task.CompletedTask; }
        public Task AddSnapshotAsync(SequenceSnapshot s, CancellationToken ct) { Snapshots.Add(s); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<OutreachEnrollment?> GetEnrollmentByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachEnrollment?>(null);
        public Task<IReadOnlyList<OutreachEnrollment>> GetDueEnrollmentsAsync(Guid t, DateTimeOffset a, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
        public Task<IReadOnlyList<OutreachEnrollment>> GetEnrollmentsByStatusAsync(Guid t, EnrollmentStatus s, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
        public Task<OutreachSequence?> GetSequenceByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachSequence?>(null);
        public Task AddSequenceAsync(OutreachSequence s, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<OutreachTemplate>> GetTemplatesAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachTemplate>>(Array.Empty<OutreachTemplate>());
        public Task AddTemplateAsync(OutreachTemplate t, CancellationToken ct) => Task.CompletedTask;
        public Task<SequenceSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<SequenceSnapshot?>(null);
        public Task<EmailActivity?> GetEmailByMessageIdAsync(Guid t, string m, CancellationToken ct) => Task.FromResult<EmailActivity?>(null);
        public Task<EmailActivity?> GetEmailBySubjectAndContactAsync(Guid t, string s, Guid c, CancellationToken ct) => Task.FromResult<EmailActivity?>(null);
        public Task AddEmailActivityAsync(EmailActivity a, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ReplyListItem>> GetRecentRepliesAsync(Guid t, int take, CancellationToken ct, bool includeSuppressed = false) => Task.FromResult<IReadOnlyList<ReplyListItem>>(Array.Empty<ReplyListItem>());
        public Task<bool> IsSuppressedAsync(Guid t, string e, CancellationToken ct) => Task.FromResult(false);
        public Task AddSuppressionAsync(SuppressionEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<OutreachEnrollment>> GetActiveEnrollmentsForContactAsync(Guid t, Guid c, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
    }

    private class FakeAuditLog : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new();
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken ct) { Events.Add(auditEvent); return Task.CompletedTask; }
        public Task<IReadOnlyList<AuditEvent>> QueryAsync(Guid t, string? et, string? ev,
            DateTimeOffset? f, DateTimeOffset? to, int l, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
    }
}
