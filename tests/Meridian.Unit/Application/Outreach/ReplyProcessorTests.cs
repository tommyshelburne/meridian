using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Outreach;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Application.Outreach;

public class ReplyProcessorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData("Re: Contact Center RFP", "Contact Center RFP")]
    [InlineData("RE: Contact Center RFP", "Contact Center RFP")]
    [InlineData("Fwd: Contact Center RFP", "Contact Center RFP")]
    [InlineData("Re: Re: Re: Contact Center RFP", "Contact Center RFP")]
    [InlineData("re: fw: Contact Center RFP", "Contact Center RFP")]
    [InlineData("AW: Beratung", "Beratung")]
    [InlineData("Contact Center RFP", "Contact Center RFP")]
    public void NormalizeSubject_strips_reply_and_forward_prefixes(string input, string expected)
    {
        ReplyProcessor.NormalizeSubject(input).Should().Be(expected);
    }

    [Fact]
    public async Task Records_reply_and_marks_enrollment_when_message_id_matches()
    {
        var (enrollment, activity) = SeedEnrollment();
        var fakes = new Fakes();
        fakes.Outreach.SeedActivity(activity);
        fakes.Outreach.SeedEnrollment(enrollment);

        var processor = new ReplyProcessor(fakes.Outreach, fakes.Contacts, fakes.Audit,
            NullLogger<ReplyProcessor>.Instance);

        var reply = new DetectedReply(activity.MessageId!, "Re: Original", DateTimeOffset.UtcNow, "rep@vendor.com");
        var result = await processor.ProcessAsync(TenantId, new[] { reply }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MatchedByMessageId.Should().Be(1);
        activity.Status.Should().Be(EmailStatus.Replied);
        enrollment.Status.Should().Be(EnrollmentStatus.Replied);
        fakes.Audit.Events.Should().ContainSingle()
            .Which.EventType.Should().Be("ReplyDetected");
    }

    [Fact]
    public async Task Falls_back_to_subject_match_when_message_id_unknown()
    {
        var contact = Contact.Create(TenantId, "Reply Sender",
            Agency.Create("Agency", AgencyType.FederalCivilian),
            ContactSource.SamGov, 0.9f, email: "rep@vendor.com");
        var (enrollment, activity) = SeedEnrollment(subject: "Contact Center RFP", contactId: contact.Id);

        var fakes = new Fakes();
        fakes.Outreach.SeedActivity(activity);
        fakes.Outreach.SeedEnrollment(enrollment);
        fakes.Contacts.Seed(contact);

        var processor = new ReplyProcessor(fakes.Outreach, fakes.Contacts, fakes.Audit,
            NullLogger<ReplyProcessor>.Instance);

        var reply = new DetectedReply("unknown-message-id", "Re: Contact Center RFP",
            DateTimeOffset.UtcNow, "rep@vendor.com");
        var result = await processor.ProcessAsync(TenantId, new[] { reply }, CancellationToken.None);

        result.Value!.MatchedBySubject.Should().Be(1);
        result.Value.MatchedByMessageId.Should().Be(0);
        enrollment.Status.Should().Be(EnrollmentStatus.Replied);
    }

    [Fact]
    public async Task Counts_unmatched_when_neither_message_id_nor_subject_match()
    {
        var fakes = new Fakes();
        var processor = new ReplyProcessor(fakes.Outreach, fakes.Contacts, fakes.Audit,
            NullLogger<ReplyProcessor>.Instance);

        var reply = new DetectedReply("orphan", "Re: Nothing", DateTimeOffset.UtcNow, "stranger@vendor.com");
        var result = await processor.ProcessAsync(TenantId, new[] { reply }, CancellationToken.None);

        result.Value!.Unmatched.Should().Be(1);
        fakes.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Subject_fallback_skipped_when_from_address_does_not_resolve_to_contact()
    {
        var (enrollment, activity) = SeedEnrollment(subject: "Contact Center RFP");
        var fakes = new Fakes();
        fakes.Outreach.SeedActivity(activity);
        fakes.Outreach.SeedEnrollment(enrollment);

        var processor = new ReplyProcessor(fakes.Outreach, fakes.Contacts, fakes.Audit,
            NullLogger<ReplyProcessor>.Instance);

        var reply = new DetectedReply("unknown", "Re: Contact Center RFP",
            DateTimeOffset.UtcNow, "stranger@vendor.com");
        var result = await processor.ProcessAsync(TenantId, new[] { reply }, CancellationToken.None);

        result.Value!.Unmatched.Should().Be(1);
        enrollment.Status.Should().Be(EnrollmentStatus.Active);
    }

    [Fact]
    public async Task Mixed_batch_aggregates_by_match_strategy()
    {
        var contact = Contact.Create(TenantId, "Sender",
            Agency.Create("A", AgencyType.FederalCivilian), ContactSource.SamGov, 0.9f, email: "x@y.com");
        var (enrollment1, activity1) = SeedEnrollment(messageId: "msg-A");
        var (enrollment2, activity2) = SeedEnrollment(messageId: "msg-B", subject: "Subject Match", contactId: contact.Id);

        var fakes = new Fakes();
        fakes.Outreach.SeedActivity(activity1);
        fakes.Outreach.SeedActivity(activity2);
        fakes.Outreach.SeedEnrollment(enrollment1);
        fakes.Outreach.SeedEnrollment(enrollment2);
        fakes.Contacts.Seed(contact);

        var processor = new ReplyProcessor(fakes.Outreach, fakes.Contacts, fakes.Audit,
            NullLogger<ReplyProcessor>.Instance);

        var replies = new[]
        {
            new DetectedReply("msg-A", "Re: Anything", DateTimeOffset.UtcNow, "rep@vendor.com"),
            new DetectedReply("orphan", "Re: Subject Match", DateTimeOffset.UtcNow, "x@y.com"),
            new DetectedReply("orphan2", "Re: Nothing", DateTimeOffset.UtcNow, "ghost@vendor.com")
        };

        var result = await processor.ProcessAsync(TenantId, replies, CancellationToken.None);

        result.Value!.MatchedByMessageId.Should().Be(1);
        result.Value.MatchedBySubject.Should().Be(1);
        result.Value.Unmatched.Should().Be(1);
        result.Value.Total.Should().Be(3);
    }

    private static (OutreachEnrollment Enrollment, EmailActivity Activity) SeedEnrollment(
        string subject = "Original", string? messageId = null, Guid? contactId = null)
    {
        var enrollment = OutreachEnrollment.Create(TenantId, Guid.NewGuid(), contactId ?? Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var activity = EmailActivity.Record(TenantId, enrollment.Id, enrollment.ContactId,
            enrollment.OpportunityId, 1, subject, "body", messageId ?? Guid.NewGuid().ToString("N"));
        return (enrollment, activity);
    }

    private class Fakes
    {
        public FakeOutreachRepository Outreach { get; } = new();
        public FakeContactRepository Contacts { get; } = new();
        public FakeAuditLog Audit { get; } = new();
    }

    private class FakeOutreachRepository : IOutreachRepository
    {
        private readonly List<EmailActivity> _activities = new();
        private readonly Dictionary<Guid, OutreachEnrollment> _enrollments = new();

        public void SeedActivity(EmailActivity activity) => _activities.Add(activity);
        public void SeedEnrollment(OutreachEnrollment e) => _enrollments[e.Id] = e;

        public Task<EmailActivity?> GetEmailByMessageIdAsync(Guid tenantId, string messageId, CancellationToken ct)
            => Task.FromResult(_activities.FirstOrDefault(a => a.MessageId == messageId));

        public Task<EmailActivity?> GetEmailBySubjectAndContactAsync(Guid tenantId, string normalizedSubject, Guid contactId, CancellationToken ct)
            => Task.FromResult(_activities
                .Where(a => a.ContactId == contactId
                    && string.Equals(a.Subject, normalizedSubject, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.SentAt)
                .FirstOrDefault());

        public Task<OutreachEnrollment?> GetEnrollmentByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_enrollments.TryGetValue(id, out var e) ? e : null);

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<bool> IsSuppressedAsync(Guid tenantId, string email, CancellationToken ct) => Task.FromResult(false);
        public Task AddSuppressionAsync(SuppressionEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<OutreachEnrollment?> GetEnrollmentAsync(Guid t, Guid c, Guid o, CancellationToken ct) => Task.FromResult<OutreachEnrollment?>(null);
        public Task<IReadOnlyList<OutreachEnrollment>> GetDueEnrollmentsAsync(Guid t, DateTimeOffset a, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
        public Task<IReadOnlyList<OutreachEnrollment>> GetEnrollmentsByStatusAsync(Guid t, EnrollmentStatus s, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
        public Task AddEnrollmentAsync(OutreachEnrollment e, CancellationToken ct) => Task.CompletedTask;
        public Task<OutreachSequence?> GetSequenceByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachSequence?>(null);
        public Task<IReadOnlyList<OutreachSequence>> GetSequencesAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachSequence>>(Array.Empty<OutreachSequence>());
        public Task AddSequenceAsync(OutreachSequence s, CancellationToken ct) => Task.CompletedTask;
        public Task<OutreachTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachTemplate?>(null);
        public Task AddTemplateAsync(OutreachTemplate t, CancellationToken ct) => Task.CompletedTask;
        public Task<SequenceSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<SequenceSnapshot?>(null);
        public Task AddSnapshotAsync(SequenceSnapshot s, CancellationToken ct) => Task.CompletedTask;
        public Task AddEmailActivityAsync(EmailActivity a, CancellationToken ct) => Task.CompletedTask;
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
