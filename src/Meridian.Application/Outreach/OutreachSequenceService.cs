using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Outreach;

namespace Meridian.Application.Outreach;

public record TemplateSummary(
    Guid Id,
    string Name,
    string SubjectTemplate,
    string BodyTemplate,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public record SequenceStepSummary(
    int StepNumber,
    int DelayDays,
    Guid TemplateId,
    string TemplateName,
    string Subject,
    TimeSpan SendWindowStart,
    TimeSpan SendWindowEnd,
    int JitterMinutes);

public record SequenceSummary(
    Guid Id,
    string Name,
    OpportunityType OpportunityType,
    AgencyType AgencyType,
    int StepCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

public record SequenceDetail(
    Guid Id,
    string Name,
    OpportunityType OpportunityType,
    AgencyType AgencyType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    IReadOnlyList<SequenceStepSummary> Steps);

public record CreateTemplateRequest(string Name, string SubjectTemplate, string BodyTemplate);

public record CreateSequenceStepRequest(
    int DelayDays,
    Guid TemplateId,
    string Subject,
    TimeSpan SendWindowStart,
    TimeSpan SendWindowEnd,
    int JitterMinutes);

public record CreateSequenceRequest(
    string Name,
    OpportunityType OpportunityType,
    AgencyType AgencyType,
    IReadOnlyList<CreateSequenceStepRequest> Steps);

// Manages OutreachSequence + OutreachTemplate CRUD for the tenant. Closes the
// soft-launch gap where these were only seedable via SQL or DevSeedService.
// Edits/deletes are deliberately omitted for v3.0 — operators can create new
// sequences and let old ones go unused (sequences carry LastUsedAt for triage).
public class OutreachSequenceService
{
    private readonly IOutreachRepository _repo;

    public OutreachSequenceService(IOutreachRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<TemplateSummary>> ListTemplatesAsync(
        Guid tenantId, CancellationToken ct)
    {
        var templates = await _repo.GetTemplatesAsync(tenantId, ct);
        return templates.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<SequenceSummary>> ListSequencesAsync(
        Guid tenantId, CancellationToken ct)
    {
        var sequences = await _repo.GetSequencesAsync(tenantId, ct);
        return sequences
            .OrderBy(s => s.Name)
            .Select(s => new SequenceSummary(
                s.Id, s.Name, s.OpportunityType, s.AgencyType,
                s.Steps.Count, s.CreatedAt, s.LastUsedAt))
            .ToList();
    }

    public async Task<SequenceDetail?> GetSequenceAsync(Guid sequenceId, CancellationToken ct)
    {
        var sequence = await _repo.GetSequenceByIdAsync(sequenceId, ct);
        if (sequence is null) return null;

        // Resolve template names for display. Templates referenced by historical
        // steps may have been replaced (no edit path yet, but defensive); fall
        // back to a sentinel string rather than throwing in the UI.
        var stepSummaries = new List<SequenceStepSummary>();
        foreach (var step in sequence.Steps.OrderBy(s => s.StepNumber))
        {
            var template = await _repo.GetTemplateByIdAsync(step.TemplateId, ct);
            stepSummaries.Add(new SequenceStepSummary(
                step.StepNumber, step.DelayDays, step.TemplateId,
                template?.Name ?? "(template missing)",
                step.Subject, step.SendWindowStart, step.SendWindowEnd,
                step.SendWindowJitterMinutes));
        }

        return new SequenceDetail(
            sequence.Id, sequence.Name, sequence.OpportunityType, sequence.AgencyType,
            sequence.CreatedAt, sequence.LastUsedAt, stepSummaries);
    }

    public async Task<ServiceResult<Guid>> CreateTemplateAsync(
        Guid tenantId, CreateTemplateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ServiceResult<Guid>.Fail("Template name is required.");
        if (string.IsNullOrWhiteSpace(request.SubjectTemplate))
            return ServiceResult<Guid>.Fail("Subject template is required.");
        if (string.IsNullOrWhiteSpace(request.BodyTemplate))
            return ServiceResult<Guid>.Fail("Body template is required.");

        try
        {
            var template = OutreachTemplate.Create(
                tenantId, request.Name.Trim(),
                request.SubjectTemplate.Trim(), request.BodyTemplate.Trim());
            await _repo.AddTemplateAsync(template, ct);
            await _repo.SaveChangesAsync(ct);
            return ServiceResult<Guid>.Ok(template.Id);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> CreateSequenceAsync(
        Guid tenantId, CreateSequenceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ServiceResult<Guid>.Fail("Sequence name is required.");
        if (request.Steps.Count == 0)
            return ServiceResult<Guid>.Fail("At least one step is required.");

        // Validate every referenced template exists for this tenant before we
        // start mutating; partial sequence creation would leave the tenant with
        // a step pointing at someone else's template (or nothing).
        for (var i = 0; i < request.Steps.Count; i++)
        {
            var step = request.Steps[i];
            var template = await _repo.GetTemplateByIdAsync(step.TemplateId, ct);
            if (template is null)
                return ServiceResult<Guid>.Fail(
                    $"Template {step.TemplateId} was not found for this workspace.");
            if (step.SendWindowEnd <= step.SendWindowStart)
                return ServiceResult<Guid>.Fail(
                    $"Step {i + 1} send window end must be after start.");
            if (step.JitterMinutes < 0)
                return ServiceResult<Guid>.Fail(
                    $"Step {i + 1} jitter cannot be negative.");
        }

        try
        {
            var sequence = OutreachSequence.Create(
                tenantId, request.Name.Trim(),
                request.OpportunityType, request.AgencyType);
            foreach (var step in request.Steps)
            {
                sequence.AddStep(
                    step.DelayDays, step.TemplateId,
                    string.IsNullOrWhiteSpace(step.Subject) ? "" : step.Subject.Trim(),
                    step.SendWindowStart, step.SendWindowEnd, step.JitterMinutes);
            }
            await _repo.AddSequenceAsync(sequence, ct);
            await _repo.SaveChangesAsync(ct);
            return ServiceResult<Guid>.Ok(sequence.Id);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    private static TemplateSummary ToSummary(OutreachTemplate t) =>
        new(t.Id, t.Name, t.SubjectTemplate, t.BodyTemplate,
            t.Version, t.CreatedAt, t.ModifiedAt);
}
