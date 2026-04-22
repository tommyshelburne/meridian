using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Sources;

namespace Meridian.Application.Sources;

public class SourceManagementService
{
    private readonly ISourceDefinitionRepository _sources;

    public SourceManagementService(ISourceDefinitionRepository sources)
    {
        _sources = sources;
    }

    public Task<IReadOnlyList<SourceDefinition>> ListForTenantAsync(Guid tenantId, CancellationToken ct)
        => _sources.GetForTenantAsync(tenantId, ct);

    public async Task<ServiceResult<SourceDefinition>> CreateAsync(
        Guid tenantId,
        SourceAdapterType adapterType,
        string name,
        string parametersJson,
        string? schedule,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ServiceResult<SourceDefinition>.Fail("Source name is required.");

        var existing = await _sources.GetForTenantAsync(tenantId, ct);
        if (existing.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<SourceDefinition>.Fail("A source with that name already exists.");

        var source = SourceDefinition.Create(tenantId, adapterType, name, parametersJson ?? "{}", schedule);
        await _sources.AddAsync(source, ct);
        await _sources.SaveChangesAsync(ct);
        return ServiceResult<SourceDefinition>.Ok(source);
    }

    public async Task<ServiceResult> UpdateParametersAsync(
        Guid tenantId, Guid sourceId, string parametersJson, string? schedule, CancellationToken ct)
    {
        var source = await _sources.GetByIdAsync(sourceId, ct);
        if (source is null || source.TenantId != tenantId)
            return ServiceResult.Fail("Source not found.");

        source.UpdateParameters(parametersJson ?? "{}");
        source.UpdateSchedule(schedule);
        await _sources.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> EnableAsync(Guid tenantId, Guid sourceId, CancellationToken ct)
    {
        var source = await _sources.GetByIdAsync(sourceId, ct);
        if (source is null || source.TenantId != tenantId)
            return ServiceResult.Fail("Source not found.");

        source.Enable();
        await _sources.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DisableAsync(Guid tenantId, Guid sourceId, CancellationToken ct)
    {
        var source = await _sources.GetByIdAsync(sourceId, ct);
        if (source is null || source.TenantId != tenantId)
            return ServiceResult.Fail("Source not found.");

        source.Disable();
        await _sources.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }
}
