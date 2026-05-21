using Meridian.Domain.Sources;

namespace Meridian.Application.Ports;

public interface ISourceDefinitionRepository
{
    Task<SourceDefinition?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Resolves a source by its global id, ignoring the tenant query filter.
    /// For the unauthenticated webhook ingest endpoint, which has no tenant
    /// context and authenticates via the per-source secret instead.
    /// </summary>
    Task<SourceDefinition?> GetByIdAcrossTenantsAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<SourceDefinition>> GetForTenantAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<SourceDefinition>> GetEnabledForTenantAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<SourceDefinition>> GetAllEnabledAcrossTenantsAsync(CancellationToken ct);
    Task AddAsync(SourceDefinition source, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
