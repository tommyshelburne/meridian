using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Opportunities;

public enum QueueDecision
{
    Pursue,
    Partner,
    Watch,
    Reject
}

public record PipelineSnapshot(
    IReadOnlyList<Opportunity> Pursuing,
    IReadOnlyList<Opportunity> Partnering,
    IReadOnlyList<Opportunity> Watching);

public class OpportunityQueueService
{
    private static readonly OpportunityStatus[] ActionableStatuses =
    {
        OpportunityStatus.Scored,
        OpportunityStatus.PendingReview,
        OpportunityStatus.Watching
    };

    private static readonly OpportunityStatus[] PipelineStatuses =
    {
        OpportunityStatus.Pursuing,
        OpportunityStatus.Partnering,
        OpportunityStatus.Watching
    };

    private readonly IOpportunityRepository _repo;

    public OpportunityQueueService(IOpportunityRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Opportunity>> GetQueueAsync(Guid tenantId, CancellationToken ct)
        => _repo.GetByStatusesAsync(tenantId, ActionableStatuses, ct);

    public async Task<PipelineSnapshot> GetPipelineAsync(Guid tenantId, CancellationToken ct)
    {
        var all = await _repo.GetByStatusesAsync(tenantId, PipelineStatuses, ct);
        return new PipelineSnapshot(
            Pursuing: all.Where(o => o.Status == OpportunityStatus.Pursuing).ToList(),
            Partnering: all.Where(o => o.Status == OpportunityStatus.Partnering).ToList(),
            Watching: all.Where(o => o.Status == OpportunityStatus.Watching).ToList());
    }

    public async Task<ServiceResult> ApplyDecisionAsync(
        Guid tenantId, Guid opportunityId, QueueDecision decision, CancellationToken ct)
    {
        var opp = await _repo.GetByIdAsync(opportunityId, ct);
        if (opp is null || opp.TenantId != tenantId)
            return ServiceResult.Fail("Opportunity not found.");

        switch (decision)
        {
            case QueueDecision.Pursue: opp.Pursue(); break;
            case QueueDecision.Partner: opp.Partner(); break;
            case QueueDecision.Watch: opp.Watch(); break;
            case QueueDecision.Reject: opp.Reject(); break;
            default: return ServiceResult.Fail("Unknown decision.");
        }

        await _repo.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }
}
