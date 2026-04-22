using Meridian.Domain.Common;
using Meridian.Domain.Outreach;

namespace Meridian.Application.Ports;

public interface IOutreachRepository
{
    Task<OutreachEnrollment?> GetEnrollmentByIdAsync(Guid id, CancellationToken ct);
    Task<OutreachEnrollment?> GetEnrollmentAsync(Guid tenantId, Guid contactId, Guid opportunityId, CancellationToken ct);
    Task<IReadOnlyList<OutreachEnrollment>> GetDueEnrollmentsAsync(Guid tenantId, DateTimeOffset asOf, CancellationToken ct);
    Task<IReadOnlyList<OutreachEnrollment>> GetEnrollmentsByStatusAsync(Guid tenantId, EnrollmentStatus status, CancellationToken ct);
    Task AddEnrollmentAsync(OutreachEnrollment enrollment, CancellationToken ct);

    Task<OutreachSequence?> GetSequenceByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<OutreachSequence>> GetSequencesAsync(Guid tenantId, CancellationToken ct);
    Task AddSequenceAsync(OutreachSequence sequence, CancellationToken ct);

    Task<OutreachTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct);
    Task AddTemplateAsync(OutreachTemplate template, CancellationToken ct);

    Task<SequenceSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken ct);
    Task AddSnapshotAsync(SequenceSnapshot snapshot, CancellationToken ct);

    Task<EmailActivity?> GetEmailByMessageIdAsync(Guid tenantId, string messageId, CancellationToken ct);
    Task<EmailActivity?> GetEmailBySubjectAndContactAsync(Guid tenantId, string normalizedSubject, Guid contactId, CancellationToken ct);
    Task AddEmailActivityAsync(EmailActivity activity, CancellationToken ct);

    Task<bool> IsSuppressedAsync(Guid tenantId, string email, CancellationToken ct);
    Task AddSuppressionAsync(SuppressionEntry entry, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
