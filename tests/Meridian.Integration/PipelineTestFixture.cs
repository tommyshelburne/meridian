using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Crm;
using Meridian.Infrastructure.Crm;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Integration;

// Shared scaffolding for the pipeline + sequence-engine integration tests.
// Uses EF InMemory rather than the SQLite fixture because SequenceEngineService
// filters on DateTimeOffset, which SQLite can't translate without a value
// converter — InMemory handles it natively without modeling-layer changes.
public sealed class PipelineTestFixture : IDisposable
{
    private readonly DbContextOptions<MeridianDbContext> _options;
    public TenantContext TenantContext { get; } = new();

    public PipelineTestFixture()
    {
        _options = new DbContextOptionsBuilder<MeridianDbContext>()
            .UseInMemoryDatabase($"pipeline-{Guid.NewGuid():N}")
            .Options;
    }

    public MeridianDbContext NewDbContext() => new(_options, TenantContext);

    public void Dispose() { }
}

internal class StubCrmAdapterFactory : ICrmAdapterFactory
{
    private readonly NoopCrmAdapter _adapter = new(NullLogger<NoopCrmAdapter>.Instance);
    public ICrmAdapter Resolve(CrmProvider provider) => _adapter;
}

// CrmConnectionService is a sealed concrete; subclassing with stub deps is the
// least-invasive way to drive its GetContextAsync to return null and exercise
// the pipeline's "no CRM configured → use Noop" fallback.
internal class NullCrmConnectionService : CrmConnectionService
{
    public NullCrmConnectionService()
        : base(new NullCrmConnectionRepository(), new NullSecretProtector(),
               new NullCrmOAuthBrokerFactory(), NullLogger<CrmConnectionService>.Instance) { }
}

internal class NullCrmConnectionRepository : ICrmConnectionRepository
{
    public Task<CrmConnection?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult<CrmConnection?>(null);
    public Task<CrmConnection?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<CrmConnection?>(null);
    public Task<IReadOnlyList<CrmConnection>> ListRefreshableExpiringBeforeAsync(
        DateTimeOffset cutoff, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrmConnection>>(Array.Empty<CrmConnection>());
    public Task AddAsync(CrmConnection connection, CancellationToken ct) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

internal class NullSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
}

internal class NullCrmOAuthBrokerFactory : ICrmOAuthBrokerFactory
{
    public ICrmOAuthBroker Resolve(CrmProvider provider) =>
        throw new InvalidOperationException("Broker resolution not exercised in pipeline tests.");
    public bool TryResolve(CrmProvider provider, out ICrmOAuthBroker broker)
    {
        broker = null!;
        return false;
    }
}
