using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Application.Sources;
using Meridian.Domain.Sources;

namespace Meridian.Unit.Application.Sources;

public class SourceManagementServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task CreateAsync_persists_and_returns_new_source()
    {
        var repo = new FakeRepo();
        var svc = new SourceManagementService(repo);

        var result = await svc.CreateAsync(TenantId, SourceAdapterType.SamGov,
            "SAM", "{\"keywords\":[\"cloud\"]}", "0 */6 * * *", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("SAM");
        result.Value.TenantId.Should().Be(TenantId);
        repo.Items.Should().ContainSingle();
        repo.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_rejects_blank_name()
    {
        var repo = new FakeRepo();
        var svc = new SourceManagementService(repo);

        var result = await svc.CreateAsync(TenantId, SourceAdapterType.SamGov,
            " ", "{}", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        repo.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_name_case_insensitive()
    {
        var repo = new FakeRepo();
        repo.Items.Add(SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "Existing", "{}"));
        var svc = new SourceManagementService(repo);

        var result = await svc.CreateAsync(TenantId, SourceAdapterType.GenericRss,
            "existing", "{}", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task UpdateParametersAsync_rejects_cross_tenant_access()
    {
        var other = Guid.NewGuid();
        var repo = new FakeRepo();
        var source = SourceDefinition.Create(other, SourceAdapterType.SamGov, "Other", "{}");
        repo.Items.Add(source);
        var svc = new SourceManagementService(repo);

        var result = await svc.UpdateParametersAsync(TenantId, source.Id, "{\"a\":1}", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        source.ParametersJson.Should().Be("{}");
    }

    [Fact]
    public async Task UpdateParametersAsync_applies_changes_for_owner()
    {
        var repo = new FakeRepo();
        var source = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        repo.Items.Add(source);
        var svc = new SourceManagementService(repo);

        var result = await svc.UpdateParametersAsync(TenantId, source.Id,
            "{\"keywords\":[\"x\"]}", "0 0 * * *", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        source.ParametersJson.Should().Be("{\"keywords\":[\"x\"]}");
        source.Schedule.Should().Be("0 0 * * *");
    }

    [Fact]
    public async Task EnableAsync_and_DisableAsync_toggle_source_state()
    {
        var repo = new FakeRepo();
        var source = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        repo.Items.Add(source);
        var svc = new SourceManagementService(repo);

        (await svc.DisableAsync(TenantId, source.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
        source.IsEnabled.Should().BeFalse();

        (await svc.EnableAsync(TenantId, source.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
        source.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnableAsync_fails_when_source_not_found()
    {
        var svc = new SourceManagementService(new FakeRepo());
        var result = await svc.EnableAsync(TenantId, Guid.NewGuid(), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    private class FakeRepo : ISourceDefinitionRepository
    {
        public List<SourceDefinition> Items { get; } = new();
        public int SaveCount { get; private set; }

        public Task<SourceDefinition?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Items.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<SourceDefinition>> GetForTenantAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SourceDefinition>>(
                Items.Where(s => s.TenantId == tenantId).ToList());

        public Task<IReadOnlyList<SourceDefinition>> GetEnabledForTenantAsync(Guid tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SourceDefinition>>(
                Items.Where(s => s.TenantId == tenantId && s.IsEnabled).ToList());

        public Task<IReadOnlyList<SourceDefinition>> GetAllEnabledAcrossTenantsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SourceDefinition>>(Items.Where(s => s.IsEnabled).ToList());

        public Task AddAsync(SourceDefinition source, CancellationToken ct)
        {
            Items.Add(source);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
