using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class OutreachRepository : IOutreachRepository
{
    private readonly MeridianDbContext _db;

    public OutreachRepository(MeridianDbContext db) => _db = db;

    public async Task<OutreachEnrollment?> GetEnrollmentByIdAsync(Guid id, CancellationToken ct)
        => await _db.OutreachEnrollments.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<OutreachEnrollment?> GetEnrollmentAsync(Guid tenantId, Guid contactId, Guid opportunityId, CancellationToken ct)
        => await _db.OutreachEnrollments.FirstOrDefaultAsync(
            e => e.ContactId == contactId && e.OpportunityId == opportunityId, ct);

    public async Task<IReadOnlyList<OutreachEnrollment>> GetDueEnrollmentsAsync(Guid tenantId, DateTimeOffset asOf, CancellationToken ct)
        => await _db.OutreachEnrollments
            .Where(e => e.Status == EnrollmentStatus.Active && e.NextSendAt != null && e.NextSendAt <= asOf)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OutreachEnrollment>> GetEnrollmentsByStatusAsync(Guid tenantId, EnrollmentStatus status, CancellationToken ct)
        => await _db.OutreachEnrollments.Where(e => e.Status == status).ToListAsync(ct);

    public async Task AddEnrollmentAsync(OutreachEnrollment enrollment, CancellationToken ct)
        => await _db.OutreachEnrollments.AddAsync(enrollment, ct);

    public async Task<OutreachSequence?> GetSequenceByIdAsync(Guid id, CancellationToken ct)
        => await _db.OutreachSequences.Include(s => s.Steps).FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<OutreachSequence>> GetSequencesAsync(Guid tenantId, CancellationToken ct)
        => await _db.OutreachSequences.Include(s => s.Steps).ToListAsync(ct);

    public async Task AddSequenceAsync(OutreachSequence sequence, CancellationToken ct)
        => await _db.OutreachSequences.AddAsync(sequence, ct);

    public async Task<OutreachTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct)
        => await _db.OutreachTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<OutreachTemplate>> GetTemplatesAsync(Guid tenantId, CancellationToken ct)
        => await _db.OutreachTemplates.OrderBy(t => t.Name).ToListAsync(ct);

    public async Task AddTemplateAsync(OutreachTemplate template, CancellationToken ct)
        => await _db.OutreachTemplates.AddAsync(template, ct);

    public async Task<SequenceSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken ct)
        => await _db.SequenceSnapshots.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddSnapshotAsync(SequenceSnapshot snapshot, CancellationToken ct)
        => await _db.SequenceSnapshots.AddAsync(snapshot, ct);

    public async Task<EmailActivity?> GetEmailByMessageIdAsync(Guid tenantId, string messageId, CancellationToken ct)
        => await _db.EmailActivities.FirstOrDefaultAsync(e => e.MessageId == messageId, ct);

    public async Task<EmailActivity?> GetEmailBySubjectAndContactAsync(Guid tenantId, string normalizedSubject, Guid contactId, CancellationToken ct)
        => await _db.EmailActivities
            .Where(e => e.ContactId == contactId && e.Subject.ToLower().Replace("re: ", "") == normalizedSubject.ToLower())
            .OrderByDescending(e => e.SentAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddEmailActivityAsync(EmailActivity activity, CancellationToken ct)
        => await _db.EmailActivities.AddAsync(activity, ct);

    public async Task<IReadOnlyList<ReplyListItem>> GetRecentRepliesAsync(
        Guid tenantId, int take, CancellationToken ct)
    {
        var rows = await (
            from activity in _db.EmailActivities.IgnoreQueryFilters()
            where activity.RepliedAt != null && activity.TenantId == tenantId
            join contact in _db.Contacts.IgnoreQueryFilters() on activity.ContactId equals contact.Id
            join opportunity in _db.Opportunities.IgnoreQueryFilters() on activity.OpportunityId equals opportunity.Id
            where contact.TenantId == tenantId && opportunity.TenantId == tenantId
            orderby activity.RepliedAt descending
            select new ReplyListItem(
                activity.Id,
                opportunity.Id,
                opportunity.Title,
                contact.Id,
                contact.FullName,
                contact.Email,
                activity.Subject,
                activity.StepNumber,
                activity.RepliedAt!.Value,
                activity.ReplyBody)
        ).Take(take).ToListAsync(ct);

        return rows;
    }

    public async Task<bool> IsSuppressedAsync(Guid tenantId, string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        var normalized = email.Trim().ToLowerInvariant();
        var atIdx = normalized.IndexOf('@');
        var domain = atIdx > 0 && atIdx < normalized.Length - 1
            ? normalized[(atIdx + 1)..]
            : null;

        return await _db.SuppressionEntries.AnyAsync(e =>
            (e.Type == SuppressionType.Email && e.Value == normalized)
            || (e.Type == SuppressionType.Domain && domain != null && e.Value == domain), ct);
    }

    public async Task AddSuppressionAsync(SuppressionEntry entry, CancellationToken ct)
        => await _db.SuppressionEntries.AddAsync(entry, ct);

    public async Task<IReadOnlyList<OutreachEnrollment>> GetActiveEnrollmentsForContactAsync(
        Guid tenantId, Guid contactId, CancellationToken ct)
        => await _db.OutreachEnrollments
            .Where(e => e.ContactId == contactId && e.Status == EnrollmentStatus.Active)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
