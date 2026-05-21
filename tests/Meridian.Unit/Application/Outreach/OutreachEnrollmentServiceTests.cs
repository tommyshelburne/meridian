using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Application.Outreach;

public class OutreachEnrollmentServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Opportunity NewOpportunity(AgencyType agencyType = AgencyType.StateLocal)
        => Opportunity.Create(TenantId, $"OPP-{Guid.NewGuid()}", OpportunitySource.SamGov,
            "Contact Center Modernization", "Body",
            Agency.Create("Utah", agencyType),
            DateTimeOffset.UtcNow);

    private static Contact EnrollableContact()
        => Contact.Create(TenantId, "Jordan Buyer",
            Agency.Create("Utah", AgencyType.StateLocal), ContactSource.Manual, 1.0f,
            email: "jordan@example.gov");

    private static OutreachSequence SeedSequenceWithStep(AgencyType agencyType, FakeOutreachRepo outreach)
    {
        var template = OutreachTemplate.Create(TenantId, "Initial", "Re: x", "Hi {{contact.first_name}}");
        var sequence = OutreachSequence.Create(TenantId, "MVP", OpportunityType.Rfp, agencyType);
        sequence.AddStep(0, template.Id, "Re: x", TimeSpan.Zero, TimeSpan.FromHours(23.99));
        outreach.SeedTemplate(template);
        outreach.SeedSequence(sequence);
        return sequence;
    }

    private static OutreachEnrollmentService CreateService(
        FakeOutreachRepo outreach, FakeContactRepo contacts, FakeAuditLog audit)
        => new(outreach, contacts, audit, NullLogger<OutreachEnrollmentService>.Instance);

    [Fact]
    public async Task Enrolls_an_enrollable_contact_into_the_matching_sequence()
    {
        var opp = NewOpportunity();
        var contact = EnrollableContact();
        opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));

        var outreach = new FakeOutreachRepo();
        var contacts = new FakeContactRepo();
        var audit = new FakeAuditLog();
        contacts.Seed(contact);
        var sequence = SeedSequenceWithStep(AgencyType.StateLocal, outreach);

        var enrolled = await CreateService(outreach, contacts, audit)
            .EnrollOpportunityAsync(opp, TenantId, CancellationToken.None);

        enrolled.Should().Be(1);
        var enrollment = outreach.Enrollments.Should().ContainSingle().Subject;
        enrollment.ContactId.Should().Be(contact.Id);
        enrollment.OpportunityId.Should().Be(opp.Id);
        enrollment.SequenceId.Should().Be(sequence.Id);
        enrollment.Status.Should().Be(EnrollmentStatus.Active);
        enrollment.NextSendAt.Should().NotBeNull();
        outreach.Snapshots.Should().ContainSingle();
        audit.Events.Should().ContainSingle(e => e.EventType == "EnrollmentCreated");
    }

    [Fact]
    public async Task Returns_zero_when_opportunity_has_no_contacts()
    {
        var opp = NewOpportunity();
        var outreach = new FakeOutreachRepo();
        SeedSequenceWithStep(AgencyType.StateLocal, outreach);

        var enrolled = await CreateService(outreach, new FakeContactRepo(), new FakeAuditLog())
            .EnrollOpportunityAsync(opp, TenantId, CancellationToken.None);

        enrolled.Should().Be(0);
        outreach.Enrollments.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_zero_when_no_sequence_is_configured()
    {
        var opp = NewOpportunity();
        var contact = EnrollableContact();
        opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));
        var contacts = new FakeContactRepo();
        contacts.Seed(contact);

        var enrolled = await CreateService(new FakeOutreachRepo(), contacts, new FakeAuditLog())
            .EnrollOpportunityAsync(opp, TenantId, CancellationToken.None);

        enrolled.Should().Be(0);
    }

    [Fact]
    public async Task Is_idempotent_when_the_contact_is_already_enrolled()
    {
        var opp = NewOpportunity();
        var contact = EnrollableContact();
        opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));

        var outreach = new FakeOutreachRepo();
        var contacts = new FakeContactRepo();
        var audit = new FakeAuditLog();
        contacts.Seed(contact);
        var sequence = SeedSequenceWithStep(AgencyType.StateLocal, outreach);
        outreach.Enrollments.Add(OutreachEnrollment.Create(
            TenantId, opp.Id, contact.Id, sequence.Id, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var enrolled = await CreateService(outreach, contacts, audit)
            .EnrollOpportunityAsync(opp, TenantId, CancellationToken.None);

        enrolled.Should().Be(0, "the contact already has an enrollment for this opportunity");
        outreach.Enrollments.Should().ContainSingle("no duplicate enrollment is created");
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_a_contact_that_is_not_enrollable()
    {
        var opp = NewOpportunity();
        var bounced = Contact.Create(TenantId, "Bounced",
            Agency.Create("Utah", AgencyType.StateLocal), ContactSource.Manual, 1.0f,
            email: "bounced@example.gov");
        bounced.MarkBounced();
        opp.AddContact(OpportunityContact.Create(opp.Id, bounced.Id));

        var outreach = new FakeOutreachRepo();
        var contacts = new FakeContactRepo();
        contacts.Seed(bounced);
        SeedSequenceWithStep(AgencyType.StateLocal, outreach);

        var enrolled = await CreateService(outreach, contacts, new FakeAuditLog())
            .EnrollOpportunityAsync(opp, TenantId, CancellationToken.None);

        enrolled.Should().Be(0, "a bounced contact is not enrollable");
        outreach.Enrollments.Should().BeEmpty();
    }

    [Fact]
    public async Task Falls_back_to_the_first_sequence_when_no_agency_type_matches()
    {
        var opp = NewOpportunity(AgencyType.StateLocal);
        var contact = EnrollableContact();
        opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));

        var outreach = new FakeOutreachRepo();
        var contacts = new FakeContactRepo();
        contacts.Seed(contact);
        // Only a FederalCivilian sequence exists — the opportunity is StateLocal.
        var sequence = SeedSequenceWithStep(AgencyType.FederalCivilian, outreach);

        var enrolled = await CreateService(outreach, contacts, new FakeAuditLog())
            .EnrollOpportunityAsync(opp, TenantId, CancellationToken.None);

        enrolled.Should().Be(1, "the first sequence is the fallback when no agency type matches");
        outreach.Enrollments.Single().SequenceId.Should().Be(sequence.Id);
    }

    [Fact]
    public async Task Snapshot_subject_falls_back_to_the_template_when_the_step_override_is_blank()
    {
        var opp = NewOpportunity();
        var contact = EnrollableContact();
        opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));

        var outreach = new FakeOutreachRepo();
        var contacts = new FakeContactRepo();
        contacts.Seed(contact);

        var template = OutreachTemplate.Create(TenantId, "Initial", "Re: {{opportunity.title}}", "Body");
        var sequence = OutreachSequence.Create(TenantId, "MVP", OpportunityType.Rfp, AgencyType.StateLocal);
        sequence.AddStep(0, template.Id, "", TimeSpan.Zero, TimeSpan.FromHours(23.99));
        outreach.SeedTemplate(template);
        outreach.SeedSequence(sequence);

        await CreateService(outreach, contacts, new FakeAuditLog())
            .EnrollOpportunityAsync(opp, TenantId, CancellationToken.None);

        var steps = JsonSerializer.Deserialize<List<SequenceStepSnapshot>>(
            outreach.Snapshots.Single().SnapshotJson)!;
        steps.Single().Subject.Should().Be("Re: {{opportunity.title}}",
            "a blank step subject override must fall back to the template's subject");
    }

    [Fact]
    public async Task Snapshot_keeps_the_step_subject_override_when_it_is_set()
    {
        var opp = NewOpportunity();
        var contact = EnrollableContact();
        opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));

        var outreach = new FakeOutreachRepo();
        var contacts = new FakeContactRepo();
        contacts.Seed(contact);

        var template = OutreachTemplate.Create(TenantId, "Initial", "Template subject", "Body");
        var sequence = OutreachSequence.Create(TenantId, "MVP", OpportunityType.Rfp, AgencyType.StateLocal);
        sequence.AddStep(0, template.Id, "Step override subject", TimeSpan.Zero, TimeSpan.FromHours(23.99));
        outreach.SeedTemplate(template);
        outreach.SeedSequence(sequence);

        await CreateService(outreach, contacts, new FakeAuditLog())
            .EnrollOpportunityAsync(opp, TenantId, CancellationToken.None);

        var steps = JsonSerializer.Deserialize<List<SequenceStepSnapshot>>(
            outreach.Snapshots.Single().SnapshotJson)!;
        steps.Single().Subject.Should().Be("Step override subject",
            "an explicit step subject override is used verbatim");
    }

    // ---- fakes ----

    private class FakeContactRepo : IContactRepository
    {
        private readonly List<Contact> _contacts = new();
        public void Seed(Contact c) => _contacts.Add(c);

        public Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_contacts.FirstOrDefault(c => c.Id == id));
        public Task<Contact?> GetByEmailAsync(Guid t, string email, CancellationToken ct)
            => Task.FromResult(_contacts.FirstOrDefault(c =>
                string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase)));
        public Task<IReadOnlyList<Contact>> GetByAgencyAsync(Guid t, string a, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>());
        public Task<IReadOnlyList<Contact>> GetUnenrichedForOpportunityAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>());
        public Task AddAsync(Contact c, CancellationToken ct) { _contacts.Add(c); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
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
