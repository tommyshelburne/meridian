using System.Text.Json;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Outreach;

// Single enrollment path shared by the automated pipeline (MeridianPipelineService)
// and the manual enrichment queue (ManualEnrichmentService). Given an opportunity
// with attached contacts, enrolls every enrollable contact into the matching
// outreach sequence. Idempotent and add-only — the caller owns the unit of work
// (SaveChanges).
public class OutreachEnrollmentService
{
    private readonly IOutreachRepository _outreachRepo;
    private readonly IContactRepository _contactRepo;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<OutreachEnrollmentService> _logger;

    public OutreachEnrollmentService(
        IOutreachRepository outreachRepo,
        IContactRepository contactRepo,
        IAuditLog auditLog,
        ILogger<OutreachEnrollmentService> logger)
    {
        _outreachRepo = outreachRepo;
        _contactRepo = contactRepo;
        _auditLog = auditLog;
        _logger = logger;
    }

    // Enrolls every enrollable contact on the opportunity into the matching
    // sequence. Skips contacts already enrolled for this opportunity, so it is
    // safe to call repeatedly. Returns the number of new enrollments created.
    public async Task<int> EnrollOpportunityAsync(
        Opportunity opportunity, Guid tenantId, CancellationToken ct)
    {
        if (opportunity.Contacts.Count == 0)
            return 0;

        var sequences = await _outreachRepo.GetSequencesAsync(tenantId, ct);
        var sequence = PickSequence(sequences, opportunity.Agency.Type);
        if (sequence is null)
        {
            _logger.LogInformation(
                "No outreach sequence configured for tenant {TenantId}; skipping enrollment for {Title}",
                tenantId, opportunity.Title);
            return 0;
        }

        if (sequence.Steps.Count == 0)
        {
            _logger.LogWarning("Sequence {SequenceId} has no steps; cannot enroll {Title}",
                sequence.Id, opportunity.Title);
            return 0;
        }

        // Capture the snapshot lazily — only once an enrollable, not-yet-enrolled
        // contact is found — so a no-op call writes nothing.
        SequenceSnapshot? snapshot = null;
        var firstSendAt = DateTimeOffset.UtcNow;
        var enrolled = 0;

        foreach (var oc in opportunity.Contacts)
        {
            var contact = await _contactRepo.GetByIdAsync(oc.ContactId, ct);
            if (contact is null || !contact.IsEnrollable) continue;

            var existing = await _outreachRepo.GetEnrollmentAsync(tenantId, contact.Id, opportunity.Id, ct);
            if (existing is not null) continue;

            snapshot ??= await CaptureSnapshotAsync(sequence, ct);

            var enrollment = OutreachEnrollment.Create(
                tenantId, opportunity.Id, contact.Id, sequence.Id, snapshot.Id, firstSendAt);
            await _outreachRepo.AddEnrollmentAsync(enrollment, ct);
            enrolled++;

            await _auditLog.AppendAsync(AuditEvent.Record(
                tenantId, "OutreachEnrollment", enrollment.Id, "EnrollmentCreated", "system",
                JsonSerializer.Serialize(new { contact.Email, opportunity.Title, SequenceId = sequence.Id })), ct);
        }

        if (enrolled > 0)
            sequence.MarkUsed();

        return enrolled;
    }

    // Prefer a sequence that explicitly matches the opportunity's agency type;
    // fall back to any tenant sequence so MVP tenants with a single generic
    // sequence still get enrollment without per-agency configuration.
    private static OutreachSequence? PickSequence(
        IReadOnlyList<OutreachSequence> sequences, AgencyType agencyType)
    {
        var match = sequences.FirstOrDefault(s => s.AgencyType == agencyType);
        return match ?? sequences.FirstOrDefault();
    }

    // Materialize each step's body from the referenced template so a later
    // template edit can't change in-flight enrollments.
    private async Task<SequenceSnapshot> CaptureSnapshotAsync(
        OutreachSequence sequence, CancellationToken ct)
    {
        var stepShapes = new List<SequenceStepSnapshot>(sequence.Steps.Count);
        var ordered = sequence.Steps.OrderBy(s => s.StepNumber).ToList();
        foreach (var step in ordered)
        {
            var template = await _outreachRepo.GetTemplateByIdAsync(step.TemplateId, ct);
            var body = template?.BodyTemplate ?? string.Empty;
            // The step's Subject is an optional override; fall back to the
            // template's subject when it is blank, as the sequence-builder UI
            // promises — otherwise the email goes out with an empty subject.
            var subject = string.IsNullOrWhiteSpace(step.Subject)
                ? template?.SubjectTemplate ?? string.Empty
                : step.Subject;
            stepShapes.Add(new SequenceStepSnapshot(
                step.StepNumber, step.DelayDays, subject, body,
                step.SendWindowStart, step.SendWindowEnd, step.SendWindowJitterMinutes));
        }

        var snapshot = SequenceSnapshot.Capture(sequence.Id, JsonSerializer.Serialize(stepShapes));
        await _outreachRepo.AddSnapshotAsync(snapshot, ct);
        return snapshot;
    }
}
