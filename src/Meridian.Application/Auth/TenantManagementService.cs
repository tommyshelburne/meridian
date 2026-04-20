using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;

namespace Meridian.Application.Auth;

public class TenantManagementService
{
    private readonly ITenantRepository _tenants;

    public TenantManagementService(ITenantRepository tenants) => _tenants = tenants;

    public async Task<ServiceResult> RenameTenantAsync(
        Guid tenantId, string newName, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return ServiceResult.Fail("Workspace not found.");
        try { tenant.Rename(newName); }
        catch (ArgumentException ex) { return ServiceResult.Fail(ex.Message); }
        await _tenants.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public Task<Tenant?> GetAsync(Guid tenantId, CancellationToken ct) =>
        _tenants.GetByIdAsync(tenantId, ct);
}
