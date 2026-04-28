using FluentAssertions;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Outreach;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Application.Outreach;

public class BounceProcessorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static BounceEvent HardBounce(string email, string? reason = "550 5.1.1") =>
        new(email, BounceEventKind.HardBounce, reason, DateTimeOffset.UtcNow);

    private static BounceEvent SoftBounce(string email) =>
        new(email, BounceEventKind.SoftBounce, "mailbox full", DateTimeOffset.UtcNow);

    private static BounceEvent Complaint(string email) =>
        new(email, BounceEventKind.SpamComplaint, null, DateTimeOffset.UtcNow);

    private static Contact MakeContact(string email = "rep@vendor.com") =>
        Contact.Create(TenantId, "Rep",
            Agency.Create("Agency", AgencyType.FederalCivilian),
            ContactSource.SamGov, 0.9f, email: email);

    private static OutreachEnrollment MakeActiveEnrollment(Guid contactId) =>
        OutreachEnrollment.Create(TenantId, Guid.NewGuid(), contactId,
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Hard_bounce_marks_contact_bounced_stops_active_enrollments_and_suppresses()
    {
        var contact = MakeContact();
        var enrollment = MakeActiveEnrollment(contact.Id);

        var fakes = new Fakes();
        fakes.Contacts.Seed(contact);
        fakes.Outreach.SeedActiveEnrollment(enrollment);

        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var result = await processor.ProcessAsync(TenantId, new[] { HardBounce(contact.Email!) }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HardBounces.Should().Be(1);
        result.Value.EnrollmentsStopped.Should().Be(1);

        contact.IsBounced.Should().BeTrue();
        enrollment.Status.Should().Be(EnrollmentStatus.Bounced);
        fakes.Outreach.Suppressions.Should().ContainSingle()
            .Which.Value.Should().Be("rep@vendor.com");
        fakes.Audit.Events.Should().ContainSingle().Which.EventType.Should().Be("HardBounce");
    }

    [Fact]
    public async Task Spam_complaint_treated_as_hard_block_with_distinct_event_type()
    {
        var contact = MakeContact();
        var fakes = new Fakes();
        fakes.Contacts.Seed(contact);

        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var result = await processor.ProcessAsync(TenantId, new[] { Complaint(contact.Email!) }, CancellationToken.None);

        result.Value!.SpamComplaints.Should().Be(1);
        contact.IsBounced.Should().BeTrue();
        fakes.Outreach.Suppressions.Should().ContainSingle();
        fakes.Audit.Events.Should().ContainSingle().Which.EventType.Should().Be("SpamComplaint");
    }

    [Fact]
    public async Task First_two_soft_bounces_only_audit_and_increment_counter()
    {
        var contact = MakeContact();
        var enrollment = MakeActiveEnrollment(contact.Id);

        var fakes = new Fakes();
        fakes.Contacts.Seed(contact);
        fakes.Outreach.SeedActiveEnrollment(enrollment);

        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var result = await processor.ProcessAsync(TenantId,
            new[] { SoftBounce(contact.Email!), SoftBounce(contact.Email!) },
            CancellationToken.None);

        result.Value!.SoftBounces.Should().Be(2);
        result.Value.EnrollmentsStopped.Should().Be(0);

        contact.SoftBounceCount.Should().Be(2);
        contact.IsBounced.Should().BeFalse();
        enrollment.Status.Should().Be(EnrollmentStatus.Active);
        fakes.Outreach.Suppressions.Should().BeEmpty();
        fakes.Audit.Events.Should().HaveCount(2)
            .And.AllSatisfy(e => e.EventType.Should().Be("SoftBounce"));
    }

    [Fact]
    public async Task Third_soft_bounce_escalates_to_hard_and_stops_enrollments()
    {
        var contact = MakeContact();
        var enrollment = MakeActiveEnrollment(contact.Id);

        var fakes = new Fakes();
        fakes.Contacts.Seed(contact);
        fakes.Outreach.SeedActiveEnrollment(enrollment);

        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var result = await processor.ProcessAsync(TenantId,
            new[]
            {
                SoftBounce(contact.Email!),
                SoftBounce(contact.Email!),
                SoftBounce(contact.Email!)
            },
            CancellationToken.None);

        result.Value!.SoftBounces.Should().Be(2);
        result.Value.HardBounces.Should().Be(1);
        result.Value.EnrollmentsStopped.Should().Be(1);

        contact.SoftBounceCount.Should().Be(3);
        contact.IsBounced.Should().BeTrue();
        enrollment.Status.Should().Be(EnrollmentStatus.Bounced);
        fakes.Outreach.Suppressions.Should().ContainSingle();

        var auditTypes = fakes.Audit.Events.Select(e => e.EventType).ToList();
        auditTypes.Should().Equal("SoftBounce", "SoftBounce", "SoftBounceEscalated");
    }

    [Fact]
    public async Task Soft_bounce_for_unknown_contact_only_audits()
    {
        var fakes = new Fakes();
        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var result = await processor.ProcessAsync(TenantId,
            new[] { SoftBounce("ghost@unknown.com") },
            CancellationToken.None);

        result.Value!.SoftBounces.Should().Be(1);
        fakes.Outreach.Suppressions.Should().BeEmpty();
        fakes.Audit.Events.Should().ContainSingle().Which.EventType.Should().Be("SoftBounce");
    }

    [Fact]
    public async Task Bounce_for_unknown_contact_still_adds_suppression()
    {
        var fakes = new Fakes();
        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var result = await processor.ProcessAsync(TenantId, new[] { HardBounce("orphan@unknown.com") }, CancellationToken.None);

        result.Value!.HardBounces.Should().Be(1);
        fakes.Outreach.Suppressions.Should().ContainSingle();
    }

    [Fact]
    public async Task Already_suppressed_email_does_not_create_duplicate_entry()
    {
        var fakes = new Fakes();
        fakes.Outreach.PreSuppress("dup@vendor.com");

        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var result = await processor.ProcessAsync(TenantId, new[] { HardBounce("dup@vendor.com") }, CancellationToken.None);

        result.Value!.HardBounces.Should().Be(1);
        fakes.Outreach.Suppressions.Should().ContainSingle();
    }

    [Fact]
    public async Task Empty_email_is_skipped_not_processed()
    {
        var fakes = new Fakes();
        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var result = await processor.ProcessAsync(TenantId, new[] { HardBounce("") }, CancellationToken.None);

        result.Value!.Skipped.Should().Be(1);
        result.Value.HardBounces.Should().Be(0);
        fakes.Outreach.Suppressions.Should().BeEmpty();
    }

    [Fact]
    public async Task Mixed_batch_aggregates_kinds_correctly()
    {
        var contact = MakeContact("rep@vendor.com");
        var fakes = new Fakes();
        fakes.Contacts.Seed(contact);

        var processor = new BounceProcessor(fakes.Contacts, fakes.Outreach, fakes.Audit,
            NullLogger<BounceProcessor>.Instance);

        var events = new[]
        {
            HardBounce("a@x.com"),
            SoftBounce("b@x.com"),
            Complaint("c@x.com"),
            HardBounce("rep@vendor.com")
        };

        var result = await processor.ProcessAsync(TenantId, events, CancellationToken.None);

        result.Value!.HardBounces.Should().Be(2);
        result.Value.SoftBounces.Should().Be(1);
        result.Value.SpamComplaints.Should().Be(1);
        result.Value.Total.Should().Be(4);
    }

    private class Fakes
    {
        public FakeContactRepository Contacts { get; } = new();
        public FakeOutreachRepository Outreach { get; } = new();
        public FakeAuditLog Audit { get; } = new();
    }

    private class FakeContactRepository : IContactRepository
    {
        private readonly List<Contact> _contacts = new();
        public void Seed(Contact c) => _contacts.Add(c);

        public Task<Contact?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct)
            => Task.FromResult(_contacts.FirstOrDefault(c =>
                string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_contacts.FirstOrDefault(c => c.Id == id));
        public Task<IReadOnlyList<Contact>> GetByAgencyAsync(Guid t, string a, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>());
        public Task<IReadOnlyList<Contact>> GetUnenrichedForOpportunityAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contact>>(Array.Empty<Contact>());
        public Task AddAsync(Contact c, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeOutreachRepository : IOutreachRepository
    {
        public List<SuppressionEntry> Suppressions { get; } = new();
        private readonly List<OutreachEnrollment> _activeEnrollments = new();

        public void SeedActiveEnrollment(OutreachEnrollment e) => _activeEnrollments.Add(e);
        public void PreSuppress(string email)
            => Suppressions.Add(SuppressionEntry.Create(TenantId, email, SuppressionType.Email, "preexisting"));

        public Task<bool> IsSuppressedAsync(Guid tenantId, string email, CancellationToken ct)
            => Task.FromResult(Suppressions.Any(s => string.Equals(s.Value, email.ToLowerInvariant(), StringComparison.Ordinal)));

        public Task AddSuppressionAsync(SuppressionEntry entry, CancellationToken ct)
        {
            Suppressions.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutreachEnrollment>> GetActiveEnrollmentsForContactAsync(
            Guid tenantId, Guid contactId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(
                _activeEnrollments.Where(e => e.ContactId == contactId && e.Status == EnrollmentStatus.Active).ToList());

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<OutreachEnrollment?> GetEnrollmentByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachEnrollment?>(null);
        public Task<OutreachEnrollment?> GetEnrollmentAsync(Guid t, Guid c, Guid o, CancellationToken ct) => Task.FromResult<OutreachEnrollment?>(null);
        public Task<IReadOnlyList<OutreachEnrollment>> GetDueEnrollmentsAsync(Guid t, DateTimeOffset a, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
        public Task<IReadOnlyList<OutreachEnrollment>> GetEnrollmentsByStatusAsync(Guid t, EnrollmentStatus s, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
        public Task AddEnrollmentAsync(OutreachEnrollment e, CancellationToken ct) => Task.CompletedTask;
        public Task<OutreachSequence?> GetSequenceByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachSequence?>(null);
        public Task<IReadOnlyList<OutreachSequence>> GetSequencesAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachSequence>>(Array.Empty<OutreachSequence>());
        public Task AddSequenceAsync(OutreachSequence s, CancellationToken ct) => Task.CompletedTask;
        public Task<OutreachTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachTemplate?>(null);
        public Task<IReadOnlyList<OutreachTemplate>> GetTemplatesAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachTemplate>>(Array.Empty<OutreachTemplate>());
        public Task AddTemplateAsync(OutreachTemplate t, CancellationToken ct) => Task.CompletedTask;
        public Task<SequenceSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<SequenceSnapshot?>(null);
        public Task AddSnapshotAsync(SequenceSnapshot s, CancellationToken ct) => Task.CompletedTask;
        public Task<EmailActivity?> GetEmailByMessageIdAsync(Guid t, string m, CancellationToken ct) => Task.FromResult<EmailActivity?>(null);
        public Task<EmailActivity?> GetEmailBySubjectAndContactAsync(Guid t, string s, Guid c, CancellationToken ct) => Task.FromResult<EmailActivity?>(null);
        public Task AddEmailActivityAsync(EmailActivity a, CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeAuditLog : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new();
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken ct)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AuditEvent>> QueryAsync(Guid tenantId, string? entityType, string? eventType,
            DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
    }
}
