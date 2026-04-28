using System.Text.Json;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Pipeline;

// Runs after IngestionOrchestrator. Picks up opportunities the worker just
// ingested (Status=New, Score=null) and walks them through scoring → POC
// enrichment → CRM deal creation → auto-enrollment, so SequenceJob has work
// to do on its next tick.
public class MeridianPipelineService
{
    private readonly IScoringEngine _scoringEngine;
    private readonly IEnumerable<IPocEnricher> _enrichers;
    private readonly ICrmAdapterFactory _crmAdapterFactory;
    private readonly CrmConnectionService _crmConnections;
    private readonly IOpportunityRepository _opportunityRepo;
    private readonly IContactRepository _contactRepo;
    private readonly IOutreachRepository _outreachRepo;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<MeridianPipelineService> _logger;

    public MeridianPipelineService(
        IScoringEngine scoringEngine,
        IEnumerable<IPocEnricher> enrichers,
        ICrmAdapterFactory crmAdapterFactory,
        CrmConnectionService crmConnections,
        IOpportunityRepository opportunityRepo,
        IContactRepository contactRepo,
        IOutreachRepository outreachRepo,
        IAuditLog auditLog,
        ILogger<MeridianPipelineService> logger)
    {
        _scoringEngine = scoringEngine;
        _enrichers = enrichers;
        _crmAdapterFactory = crmAdapterFactory;
        _crmConnections = crmConnections;
        _opportunityRepo = opportunityRepo;
        _contactRepo = contactRepo;
        _outreachRepo = outreachRepo;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<ServiceResult<PipelineRunSummary>> ProcessNewOpportunitiesAsync(
        Guid tenantId, CancellationToken ct)
    {
        var summary = new PipelineRunSummary();

        var opportunities = await _opportunityRepo.GetByStatusAsync(tenantId, OpportunityStatus.New, ct);
        if (opportunities.Count == 0)
            return ServiceResult<PipelineRunSummary>.Ok(summary);

        var (crm, crmCtx) = await ResolveCrmAdapterAsync(tenantId, ct);
        var sequences = await _outreachRepo.GetSequencesAsync(tenantId, ct);

        // Capture one snapshot per sequence per run; reused across all enrollments
        // chosen for that sequence so contacts on the same opportunity share an
        // identical step list (spec §11.3 snapshot-on-enrollment).
        var snapshotsByseq = new Dictionary<Guid, SequenceSnapshot>();

        foreach (var opp in opportunities)
        {
            var scoringResult = _scoringEngine.Score(opp);
            opp.SetSeatEstimate(scoringResult.SeatEstimate);
            opp.ApplyScore(scoringResult.Score);
            var score = scoringResult.Score;

            await _auditLog.AppendAsync(AuditEvent.Record(
                tenantId, "Opportunity", opp.Id, "OpportunityScored", "system",
                JsonSerializer.Serialize(new
                {
                    opp.Title,
                    score.Total,
                    score.Verdict,
                    Breakdown = score.Breakdown,
                    SeatEstimate = scoringResult.SeatEstimate,
                    score.RecompeteDetected
                })), ct);

            if (score.Verdict == ScoreVerdict.NoBid)
            {
                summary.NoBid++;
                continue;
            }

            await EnrichContactsAsync(opp, tenantId, summary, ct);

            await CreateCrmDealAsync(opp, tenantId, crm, crmCtx, summary, ct);

            await EnrollContactsAsync(opp, tenantId, sequences, snapshotsByseq, summary, ct);

            if (score.Verdict == ScoreVerdict.Pursue) summary.Pursue++;
            else summary.Partner++;

            summary.Processed++;
        }

        await _opportunityRepo.SaveChangesAsync(ct);
        await _contactRepo.SaveChangesAsync(ct);
        await _outreachRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Pipeline processed {Processed} opportunities for tenant {TenantId}: " +
            "{Pursue} pursue, {Partner} partner, {NoBid} no-bid, " +
            "{ContactsEnriched} contacts, {DealsCreated} deals, {Enrollments} enrollments",
            summary.Processed, tenantId, summary.Pursue, summary.Partner, summary.NoBid,
            summary.ContactsEnriched, summary.DealsCreated, summary.Enrollments);

        return ServiceResult<PipelineRunSummary>.Ok(summary);
    }

    private async Task EnrichContactsAsync(
        Opportunity opp, Guid tenantId, PipelineRunSummary summary, CancellationToken ct)
    {
        foreach (var enricher in _enrichers)
        {
            ServiceResult<IReadOnlyList<Contact>> enrichResult;
            try
            {
                enrichResult = await enricher.EnrichAsync(opp, tenantId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Enricher {Enricher} threw for opportunity {OpportunityId}",
                    enricher.SourceName, opp.Id);
                continue;
            }
            if (!enrichResult.IsSuccess) continue;

            foreach (var contact in enrichResult.Value!)
            {
                var existing = contact.Email is not null
                    ? await _contactRepo.GetByEmailAsync(tenantId, contact.Email, ct)
                    : null;

                if (existing is null)
                {
                    await _contactRepo.AddAsync(contact, ct);
                    opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));
                    summary.ContactsEnriched++;
                }
                else
                {
                    opp.AddContact(OpportunityContact.Create(opp.Id, existing.Id));
                }
            }

            // First enricher that returned at least one contact wins; mirrors the
            // ordering-as-priority convention used elsewhere in DI registration.
            if (opp.Contacts.Count > 0) break;
        }
    }

    private async Task CreateCrmDealAsync(
        Opportunity opp, Guid tenantId, ICrmAdapter crm, CrmConnectionContext crmCtx,
        PipelineRunSummary summary, CancellationToken ct)
    {
        var orgResult = await crm.FindOrCreateOrganizationAsync(crmCtx, opp.Agency.Name, ct);
        if (!orgResult.IsSuccess)
        {
            _logger.LogWarning("CRM organization create failed for {Agency}: {Error}",
                opp.Agency.Name, orgResult.Error);
            return;
        }

        var dealResult = await crm.CreateDealAsync(crmCtx, opp, orgResult.Value!, ct);
        if (!dealResult.IsSuccess)
        {
            _logger.LogWarning("CRM deal create failed for {Title}: {Error}", opp.Title, dealResult.Error);
            return;
        }

        summary.DealsCreated++;
        await _auditLog.AppendAsync(AuditEvent.Record(
            tenantId, "Opportunity", opp.Id, "DealCreated", "system",
            JsonSerializer.Serialize(new { DealId = dealResult.Value, opp.Title })), ct);
    }

    private async Task EnrollContactsAsync(
        Opportunity opp, Guid tenantId,
        IReadOnlyList<OutreachSequence> sequences,
        Dictionary<Guid, SequenceSnapshot> snapshotsByseq,
        PipelineRunSummary summary, CancellationToken ct)
    {
        if (opp.Contacts.Count == 0) return;

        var sequence = PickSequence(sequences, opp.Agency.Type);
        if (sequence is null)
        {
            _logger.LogInformation("No outreach sequence configured for tenant {TenantId}; skipping enrollment for {Title}",
                tenantId, opp.Title);
            return;
        }

        if (sequence.Steps.Count == 0)
        {
            _logger.LogWarning("Sequence {SequenceId} has no steps; cannot enroll {Title}",
                sequence.Id, opp.Title);
            return;
        }

        if (!snapshotsByseq.TryGetValue(sequence.Id, out var snapshot))
        {
            snapshot = await CaptureSnapshotAsync(sequence, ct);
            snapshotsByseq[sequence.Id] = snapshot;
        }

        var firstSendAt = DateTimeOffset.UtcNow;
        foreach (var oc in opp.Contacts)
        {
            var contact = await _contactRepo.GetByIdAsync(oc.ContactId, ct);
            if (contact is null || !contact.IsEnrollable) continue;

            var existing = await _outreachRepo.GetEnrollmentAsync(tenantId, contact.Id, opp.Id, ct);
            if (existing is not null) continue;

            var enrollment = OutreachEnrollment.Create(
                tenantId, opp.Id, contact.Id, sequence.Id, snapshot.Id, firstSendAt);
            await _outreachRepo.AddEnrollmentAsync(enrollment, ct);
            summary.Enrollments++;

            await _auditLog.AppendAsync(AuditEvent.Record(
                tenantId, "OutreachEnrollment", enrollment.Id, "EnrollmentCreated", "system",
                JsonSerializer.Serialize(new { contact.Email, opp.Title, SequenceId = sequence.Id })), ct);
        }

        sequence.MarkUsed();
    }

    private async Task<SequenceSnapshot> CaptureSnapshotAsync(
        OutreachSequence sequence, CancellationToken ct)
    {
        // Materialize each step's body from the referenced template so a later
        // template edit can't change in-flight enrollments.
        var stepShapes = new List<SequenceStepSnapshot>(sequence.Steps.Count);
        var ordered = sequence.Steps.OrderBy(s => s.StepNumber).ToList();
        foreach (var step in ordered)
        {
            var template = await _outreachRepo.GetTemplateByIdAsync(step.TemplateId, ct);
            var body = template?.BodyTemplate ?? string.Empty;
            stepShapes.Add(new SequenceStepSnapshot(
                step.StepNumber, step.DelayDays, step.Subject, body,
                step.SendWindowStart, step.SendWindowEnd, step.SendWindowJitterMinutes));
        }

        var snapshot = SequenceSnapshot.Capture(sequence.Id, JsonSerializer.Serialize(stepShapes));
        await _outreachRepo.AddSnapshotAsync(snapshot, ct);
        return snapshot;
    }

    private static OutreachSequence? PickSequence(
        IReadOnlyList<OutreachSequence> sequences, AgencyType agencyType)
    {
        // Prefer a sequence that explicitly matches the opportunity's agency type;
        // fall back to any tenant sequence so MVP tenants with a single generic
        // sequence still get enrollment without per-agency configuration.
        var match = sequences.FirstOrDefault(s => s.AgencyType == agencyType);
        return match ?? sequences.FirstOrDefault();
    }

    private async Task<(ICrmAdapter Adapter, CrmConnectionContext Ctx)> ResolveCrmAdapterAsync(
        Guid tenantId, CancellationToken ct)
    {
        var ctx = await _crmConnections.GetContextAsync(tenantId, ct)
                  ?? CrmConnectionContext.None(tenantId);
        try
        {
            return (_crmAdapterFactory.Resolve(ctx.Provider), ctx);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "No adapter registered for tenant {TenantId} provider {Provider}; falling back to Noop",
                tenantId, ctx.Provider);
            return (_crmAdapterFactory.Resolve(CrmProvider.None), CrmConnectionContext.None(tenantId));
        }
    }
}

public record PipelineRunSummary
{
    public int Processed { get; set; }
    public int Pursue { get; set; }
    public int Partner { get; set; }
    public int NoBid { get; set; }
    public int ContactsEnriched { get; set; }
    public int DealsCreated { get; set; }
    public int Enrollments { get; set; }
}
