using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Ingestion;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Sources;
using Meridian.Domain.Tenants;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Application.Ingestion;

public class IngestionOrchestratorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Fetches_dedupes_and_saves_new_opportunities_per_source()
    {
        var source = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");

        var sourceRepo = new FakeSourceRepo(new[] { source });
        var oppRepo = new FakeOpportunityRepo();
        var adapter = new FakeAdapter(SourceAdapterType.SamGov, new[]
        {
            new IngestedOpportunity("EXT-1", "First", "desc1",
                "HHS", AgencyType.FederalCivilian, null,
                DateTimeOffset.UtcNow, null, null, null, null),
            new IngestedOpportunity("EXT-2", "Second", "desc2",
                "HHS", AgencyType.FederalCivilian, null,
                DateTimeOffset.UtcNow, null, null, null, null)
        });
        var factory = new FakeFactory(adapter);
        var tenantContext = new FakeTenantContext();

        var orch = new IngestionOrchestrator(
            sourceRepo, factory, oppRepo, tenantContext,
            NullLogger<IngestionOrchestrator>.Instance);

        var result = await orch.RunForTenantAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Ingested.Should().Be(2);
        result.Value.Duplicates.Should().Be(0);
        result.Value.FailedSources.Should().Be(0);
        oppRepo.Added.Should().HaveCount(2);
        oppRepo.Added[0].SourceDefinitionId.Should().Be(source.Id);
        source.LastRunStatus.Should().Be(SourceRunStatus.Succeeded);
    }

    [Fact]
    public async Task Skips_duplicate_opportunities_already_persisted()
    {
        var source = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        var existing = Opportunity.Create(
            TenantId, "EXT-DUP", OpportunitySource.SamGov,
            "Existing", "desc", Agency.Create("HHS", AgencyType.FederalCivilian),
            DateTimeOffset.UtcNow, sourceDefinitionId: source.Id);

        var sourceRepo = new FakeSourceRepo(new[] { source });
        var oppRepo = new FakeOpportunityRepo();
        oppRepo.Seed(existing);

        var adapter = new FakeAdapter(SourceAdapterType.SamGov, new[]
        {
            new IngestedOpportunity("EXT-DUP", "Duplicate", "desc",
                "HHS", AgencyType.FederalCivilian, null,
                DateTimeOffset.UtcNow, null, null, null, null),
            new IngestedOpportunity("EXT-NEW", "New", "desc",
                "HHS", AgencyType.FederalCivilian, null,
                DateTimeOffset.UtcNow, null, null, null, null)
        });

        var orch = new IngestionOrchestrator(sourceRepo, new FakeFactory(adapter), oppRepo,
            new FakeTenantContext(), NullLogger<IngestionOrchestrator>.Instance);

        var result = await orch.RunForTenantAsync(TenantId, CancellationToken.None);

        result.Value!.Ingested.Should().Be(1);
        result.Value.Duplicates.Should().Be(1);
        oppRepo.Added.Should().HaveCount(1);
        oppRepo.Added[0].ExternalId.Should().Be("EXT-NEW");
    }

    [Fact]
    public async Task Marks_source_failed_when_adapter_returns_error()
    {
        var source = SourceDefinition.Create(TenantId, SourceAdapterType.GenericRss, "Feed", "{}");
        var sourceRepo = new FakeSourceRepo(new[] { source });
        var adapter = new FakeAdapter(SourceAdapterType.GenericRss, failError: "boom");

        var orch = new IngestionOrchestrator(sourceRepo, new FakeFactory(adapter),
            new FakeOpportunityRepo(), new FakeTenantContext(),
            NullLogger<IngestionOrchestrator>.Instance);

        var result = await orch.RunForTenantAsync(TenantId, CancellationToken.None);

        result.Value!.FailedSources.Should().Be(1);
        source.LastRunStatus.Should().Be(SourceRunStatus.Failed);
        source.LastRunError.Should().Be("boom");
        source.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task Auto_disables_source_after_repeated_failures()
    {
        var source = SourceDefinition.Create(TenantId, SourceAdapterType.GenericRss, "Feed", "{}");
        var sourceRepo = new FakeSourceRepo(new[] { source });
        var adapter = new FakeAdapter(SourceAdapterType.GenericRss, failError: "boom");
        var orch = new IngestionOrchestrator(sourceRepo, new FakeFactory(adapter),
            new FakeOpportunityRepo(), new FakeTenantContext(),
            NullLogger<IngestionOrchestrator>.Instance);

        for (var i = 0; i < SourceDefinition.AutoDisableAfterConsecutiveFailures; i++)
            await orch.RunForTenantAsync(TenantId, CancellationToken.None);

        source.IsEnabled.Should().BeFalse();
        source.LastRunStatus.Should().Be(SourceRunStatus.Disabled);
    }

    [Fact]
    public async Task Only_runs_enabled_sources()
    {
        var enabled = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "Enabled", "{}");
        var disabled = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "Disabled", "{}");
        disabled.Disable();

        var sourceRepo = new FakeSourceRepo(new[] { enabled });
        var adapter = new FakeAdapter(SourceAdapterType.SamGov, new[]
        {
            new IngestedOpportunity("E-1", "From enabled", "desc",
                "HHS", AgencyType.FederalCivilian, null,
                DateTimeOffset.UtcNow, null, null, null, null)
        });

        var orch = new IngestionOrchestrator(sourceRepo, new FakeFactory(adapter),
            new FakeOpportunityRepo(), new FakeTenantContext(),
            NullLogger<IngestionOrchestrator>.Instance);

        var result = await orch.RunForTenantAsync(TenantId, CancellationToken.None);

        result.Value!.Ingested.Should().Be(1);
    }

    private class FakeSourceRepo : ISourceDefinitionRepository
    {
        private readonly List<SourceDefinition> _sources;
        public FakeSourceRepo(IEnumerable<SourceDefinition> sources) => _sources = sources.ToList();

        public Task<SourceDefinition?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_sources.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<SourceDefinition>> GetForTenantAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SourceDefinition>>(_sources.Where(s => s.TenantId == tenantId).ToList());

        public Task<IReadOnlyList<SourceDefinition>> GetEnabledForTenantAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SourceDefinition>>(
                _sources.Where(s => s.TenantId == tenantId && s.IsEnabled).ToList());

        public Task<IReadOnlyList<SourceDefinition>> GetAllEnabledAcrossTenantsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SourceDefinition>>(_sources.Where(s => s.IsEnabled).ToList());

        public Task AddAsync(SourceDefinition source, CancellationToken ct)
        {
            _sources.Add(source);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeOpportunityRepo : IOpportunityRepository
    {
        public List<Opportunity> Added { get; } = new();
        private readonly List<Opportunity> _seeded = new();

        public void Seed(Opportunity opp) => _seeded.Add(opp);

        public Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_seeded.Concat(Added).FirstOrDefault(o => o.Id == id));

        public Task<Opportunity?> GetByExternalIdAsync(Guid tenantId, string externalId, CancellationToken ct)
            => Task.FromResult(_seeded.Concat(Added)
                .FirstOrDefault(o => o.TenantId == tenantId && o.ExternalId == externalId));

        public Task<Opportunity?> GetBySourceExternalIdAsync(
            Guid tenantId, Guid sourceDefinitionId, string externalId, CancellationToken ct)
            => Task.FromResult(_seeded.Concat(Added).FirstOrDefault(o =>
                o.TenantId == tenantId
                && o.SourceDefinitionId == sourceDefinitionId
                && o.ExternalId == externalId));

        public Task<IReadOnlyList<Opportunity>> GetByStatusesAsync(Guid t, IReadOnlyCollection<OpportunityStatus> s, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Opportunity>>(Array.Empty<Opportunity>());

        public Task<IReadOnlyList<Opportunity>> GetByStatusAsync(Guid tenantId, OpportunityStatus status, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Opportunity>>(
                _seeded.Concat(Added).Where(o => o.TenantId == tenantId && o.Status == status).ToList());

        public Task<IReadOnlyList<Opportunity>> GetWatchedAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Opportunity>>(
                _seeded.Concat(Added).Where(o => o.TenantId == tenantId && o.WatchedSince != null).ToList());

        public Task AddAsync(Opportunity opportunity, CancellationToken ct)
        {
            Added.Add(opportunity);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeAdapter : IOpportunitySourceAdapter
    {
        private readonly IReadOnlyList<IngestedOpportunity>? _results;
        private readonly string? _failError;

        public SourceAdapterType AdapterType { get; }

        public FakeAdapter(SourceAdapterType type,
            IReadOnlyList<IngestedOpportunity>? results = null,
            string? failError = null)
        {
            AdapterType = type;
            _results = results;
            _failError = failError;
        }

        public Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
            SourceDefinition source, CancellationToken ct)
        {
            return Task.FromResult(_failError is not null
                ? ServiceResult<IReadOnlyList<IngestedOpportunity>>.Fail(_failError)
                : ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(_results ?? Array.Empty<IngestedOpportunity>()));
        }
    }

    private class FakeFactory : ISourceAdapterFactory
    {
        private readonly IOpportunitySourceAdapter _adapter;
        public FakeFactory(IOpportunitySourceAdapter adapter) => _adapter = adapter;
        public IOpportunitySourceAdapter Resolve(SourceAdapterType adapterType) => _adapter;
    }

    private class FakeTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; } = Guid.Empty;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
