namespace Meridian.Domain.Tenants;

public interface ITenantContext
{
    Guid TenantId { get; }
    void SetTenant(Guid tenantId);
}
