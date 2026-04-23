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

public class OpportunityQueueService
{
    private static readonly OpportunityStatus[] ActionableStatuses =
    {
        OpportunityStatus.Scored,
        OpportunityStatus.PendingReview,
        OpportunityStatus.Watching
    };

    private readonly IOpportunityRepository _repo;

    public OpportunityQueueService(IOpportunityRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Opportunity>> GetQueueAsync(Guid tenantId, CancellationToken ct)
        => _repo.GetByStatusesAsync(tenantId, ActionableStatuses, ct);

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
