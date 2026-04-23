using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Outreach;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure.Outreach;

public class SuppressionFilterEmailSenderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly RecordingEmailSender _inner = new();
    private readonly StubOutreachRepository _repo = new();
    private readonly StubTenantContext _tenant = new(TenantId);

    private SuppressionFilterEmailSender Build() =>
        new(_inner, _repo, _tenant, NullLogger<SuppressionFilterEmailSender>.Instance);

    private static EmailMessage Msg(string to = "target@example.com") =>
        new(to, "from@x.com", "From", "Subject", "<p>Body</p>");

    [Fact]
    public async Task Sends_when_email_not_suppressed()
    {
        var result = await Build().SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _inner.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task Blocks_when_email_directly_suppressed()
    {
        _repo.AddEmailSuppression("target@example.com");

        var result = await Build().SendAsync(Msg(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(SuppressionFilterEmailSender.SuppressedError);
        _inner.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Blocks_when_domain_suppressed()
    {
        _repo.AddDomainSuppression("example.com");

        var result = await Build().SendAsync(Msg("anyone@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(SuppressionFilterEmailSender.SuppressedError);
    }

    [Fact]
    public async Task Email_match_is_case_insensitive()
    {
        _repo.AddEmailSuppression("Target@Example.COM");

        var result = await Build().SendAsync(Msg("target@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    private class StubTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public StubTenantContext(Guid id) => TenantId = id;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private class StubOutreachRepository : IOutreachRepository
    {
        private readonly HashSet<string> _emails = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);

        public void AddEmailSuppression(string email) => _emails.Add(email.Trim().ToLowerInvariant());
        public void AddDomainSuppression(string domain) => _domains.Add(domain.Trim().ToLowerInvariant());

        public Task<bool> IsSuppressedAsync(Guid tenantId, string email, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(email)) return Task.FromResult(false);
            var normalized = email.Trim().ToLowerInvariant();
            if (_emails.Contains(normalized)) return Task.FromResult(true);
            var atIdx = normalized.IndexOf('@');
            if (atIdx > 0 && atIdx < normalized.Length - 1)
            {
                var domain = normalized[(atIdx + 1)..];
                if (_domains.Contains(domain)) return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task AddSuppressionAsync(SuppressionEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<OutreachEnrollment>> GetActiveEnrollmentsForContactAsync(Guid t, Guid c, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());

        public Task<OutreachEnrollment?> GetEnrollmentByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachEnrollment?>(null);
        public Task<OutreachEnrollment?> GetEnrollmentAsync(Guid tenantId, Guid contactId, Guid opportunityId, CancellationToken ct) => Task.FromResult<OutreachEnrollment?>(null);
        public Task<IReadOnlyList<OutreachEnrollment>> GetDueEnrollmentsAsync(Guid tenantId, DateTimeOffset asOf, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
        public Task<IReadOnlyList<OutreachEnrollment>> GetEnrollmentsByStatusAsync(Guid tenantId, EnrollmentStatus status, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
        public Task AddEnrollmentAsync(OutreachEnrollment enrollment, CancellationToken ct) => Task.CompletedTask;
        public Task<OutreachSequence?> GetSequenceByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachSequence?>(null);
        public Task<IReadOnlyList<OutreachSequence>> GetSequencesAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachSequence>>(Array.Empty<OutreachSequence>());
        public Task AddSequenceAsync(OutreachSequence sequence, CancellationToken ct) => Task.CompletedTask;
        public Task<OutreachTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachTemplate?>(null);
        public Task AddTemplateAsync(OutreachTemplate template, CancellationToken ct) => Task.CompletedTask;
        public Task<SequenceSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<SequenceSnapshot?>(null);
        public Task AddSnapshotAsync(SequenceSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;
        public Task<EmailActivity?> GetEmailByMessageIdAsync(Guid tenantId, string messageId, CancellationToken ct) => Task.FromResult<EmailActivity?>(null);
        public Task<EmailActivity?> GetEmailBySubjectAndContactAsync(Guid tenantId, string normalizedSubject, Guid contactId, CancellationToken ct) => Task.FromResult<EmailActivity?>(null);
        public Task AddEmailActivityAsync(EmailActivity activity, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
