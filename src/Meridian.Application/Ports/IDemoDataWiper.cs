namespace Meridian.Application.Ports;

// Deletes every tenant-owned data row for a demo tenant so it can be
// re-seeded to a pristine state. Deliberately leaves the Tenant itself,
// its Users/memberships, and auth tokens in place — reset recycles demo
// data, not the operator's login. Callers are responsible for verifying
// the tenant is actually a demo tenant before invoking.
public interface IDemoDataWiper
{
    Task<int> WipeTenantDataAsync(Guid tenantId, CancellationToken ct);
}
