using Meridian.Domain.Tenants;

namespace Meridian.Infrastructure.Persistence;

public class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
