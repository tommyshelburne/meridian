using FluentAssertions;
using Meridian.Application.Opportunities;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Unit.Application.Opportunities;

public class OpportunityQueueServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Opportunity NewScoredOpportunity(int total = 12)
    {
        var opp = Opportunity.Create(TenantId, $"X-{Guid.NewGuid()}", OpportunitySource.SamGov,
            "Contact Center RFP", "Body",
            Agency.Create("VA", AgencyType.FederalCivilian),
            DateTimeOffset.UtcNow);
        opp.ApplyScore(BidScore.Create(total, total >= 10 ? ScoreVerdict.Pursue : ScoreVerdict.Partner));
        return opp;
    }

    [Theory]
    [InlineData(QueueDecision.Pursue, OpportunityStatus.Pursuing)]
    [InlineData(QueueDecision.Partner, OpportunityStatus.Partnering)]
    [InlineData(QueueDecision.Watch, OpportunityStatus.Watching)]
    [InlineData(QueueDecision.Reject, OpportunityStatus.Rejected)]
    public async Task ApplyDecision_routes_to_the_matching_aggregate_method(
        QueueDecision decision, OpportunityStatus expected)
    {
        var opp = NewScoredOpportunity();
        var repo = new FakeRepo(opp);
        var svc = new OpportunityQueueService(repo);

        var result = await svc.ApplyDecisionAsync(TenantId, opp.Id, decision, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        opp.Status.Should().Be(expected);
        repo.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task ApplyDecision_fails_when_opportunity_not_found()
    {
        var svc = new OpportunityQueueService(new FakeRepo(null));
        var result = await svc.ApplyDecisionAsync(TenantId, Guid.NewGuid(), QueueDecision.Pursue, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ApplyDecision_rejects_cross_tenant_access()
    {
        var opp = NewScoredOpportunity();
        var repo = new FakeRepo(opp);
        var svc = new OpportunityQueueService(repo);

        var otherTenant = Guid.NewGuid();
        var result = await svc.ApplyDecisionAsync(otherTenant, opp.Id, QueueDecision.Pursue, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        opp.Status.Should().Be(OpportunityStatus.Scored);
        repo.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task GetQueue_filters_to_actionable_statuses()
    {
        var repo = new FakeRepo(null);
        var svc = new OpportunityQueueService(repo);
        await svc.GetQueueAsync(TenantId, CancellationToken.None);

        repo.LastQueriedStatuses.Should().BeEquivalentTo(new[]
        {
            OpportunityStatus.Scored,
            OpportunityStatus.PendingReview,
            OpportunityStatus.Watching
        });
    }

    private class FakeRepo : IOpportunityRepository
    {
        private readonly Opportunity? _opp;
        public int SaveCount { get; private set; }
        public IReadOnlyCollection<OpportunityStatus>? LastQueriedStatuses { get; private set; }

        public FakeRepo(Opportunity? opp) => _opp = opp;

        public Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(_opp);

        public Task<IReadOnlyList<Opportunity>> GetByStatusesAsync(
            Guid tenantId, IReadOnlyCollection<OpportunityStatus> statuses, CancellationToken ct)
        {
            LastQueriedStatuses = statuses;
            return Task.FromResult<IReadOnlyList<Opportunity>>(Array.Empty<Opportunity>());
        }

        public Task SaveChangesAsync(CancellationToken ct) { SaveCount++; return Task.CompletedTask; }

        public Task<Opportunity?> GetByExternalIdAsync(Guid t, string e, CancellationToken ct) => Task.FromResult<Opportunity?>(null);
        public Task<Opportunity?> GetBySourceExternalIdAsync(Guid t, Guid s, string e, CancellationToken ct) => Task.FromResult<Opportunity?>(null);
        public Task<IReadOnlyList<Opportunity>> GetByStatusAsync(Guid t, OpportunityStatus s, CancellationToken ct) => Task.FromResult<IReadOnlyList<Opportunity>>(Array.Empty<Opportunity>());
        public Task<IReadOnlyList<Opportunity>> GetWatchedAsync(Guid t, CancellationToken ct) => Task.FromResult<IReadOnlyList<Opportunity>>(Array.Empty<Opportunity>());
        public Task AddAsync(Opportunity o, CancellationToken ct) => Task.CompletedTask;
    }
}
