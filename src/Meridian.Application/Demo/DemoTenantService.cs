using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Meridian.Domain.Users;

namespace Meridian.Application.Demo;

// Provisions and recycles isolated demo tenants for prospect walkthroughs.
// Every operation is gated on the "demo-" slug prefix so neither provision
// nor — far more importantly — reset can ever touch a real customer tenant.
public class DemoTenantService
{
    public const string SlugPrefix = "demo-";
    public const string NotDemoSlugError =
        "Demo operations require a tenant slug starting with 'demo-'.";

    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IUserTenantRepository _memberships;
    private readonly IPasswordHasher _passwords;
    private readonly ITenantContext _tenantContext;
    private readonly DemoSeedService _seed;
    private readonly IDemoDataWiper _wiper;

    public DemoTenantService(
        ITenantRepository tenants,
        IUserRepository users,
        IUserTenantRepository memberships,
        IPasswordHasher passwords,
        ITenantContext tenantContext,
        DemoSeedService seed,
        IDemoDataWiper wiper)
    {
        _tenants = tenants;
        _users = users;
        _memberships = memberships;
        _passwords = passwords;
        _tenantContext = tenantContext;
        _seed = seed;
        _wiper = wiper;
    }

    public async Task<ServiceResult<DemoProvisionResult>> ProvisionAsync(
        DemoProvisionRequest request, CancellationToken ct)
    {
        if (!IsDemoSlug(request.Slug))
            return ServiceResult<DemoProvisionResult>.Fail(NotDemoSlugError);

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12)
            return ServiceResult<DemoProvisionResult>.Fail("Password must be at least 12 characters.");

        var tenant = await _tenants.GetBySlugAsync(request.Slug, ct);
        var alreadyExisted = tenant is not null;
        if (tenant is null)
        {
            try
            {
                // Pro plan so the demo never shows trial banners or limits.
                tenant = Tenant.Create(request.TenantName, request.Slug, PlanTier.Pro);
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<DemoProvisionResult>.Fail(ex.Message);
            }
            await _tenants.AddAsync(tenant, ct);
        }

        // Repositories read through the tenant query filters from here on.
        _tenantContext.SetTenant(tenant.Id);

        var user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is null)
        {
            try
            {
                user = User.CreateWithPassword(request.Email, request.FullName,
                    _passwords.Hash(request.Password));
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<DemoProvisionResult>.Fail(ex.Message);
            }
            // The operator logs straight in — no verification round-trip, and
            // no verification email exists to send (outbound is sandboxed).
            user.VerifyEmail();
            await _users.AddAsync(user, ct);
        }

        var membership = await _memberships.GetAsync(user.Id, tenant.Id, ct);
        if (membership is null)
            await _memberships.AddAsync(UserTenant.CreateOwner(user.Id, tenant.Id), ct);

        await _tenants.SaveChangesAsync(ct);

        var seeded = await _seed.SeedAsync(tenant.Id, ct);
        if (!seeded.IsSuccess)
            return ServiceResult<DemoProvisionResult>.Fail(seeded.Error!);

        return ServiceResult<DemoProvisionResult>.Ok(new DemoProvisionResult(
            tenant.Id, user.Id, tenant.Slug, alreadyExisted, seeded.Value!));
    }

    // Wipes all demo data and re-seeds the pristine dataset. The tenant, its
    // users, and memberships survive — only the demo story is recycled.
    public async Task<ServiceResult<DemoSeedSummary>> ResetAsync(string slug, CancellationToken ct)
    {
        if (!IsDemoSlug(slug))
            return ServiceResult<DemoSeedSummary>.Fail(NotDemoSlugError);

        var tenant = await _tenants.GetBySlugAsync(slug, ct);
        if (tenant is null)
            return ServiceResult<DemoSeedSummary>.Fail($"No tenant with slug '{slug}'.");

        _tenantContext.SetTenant(tenant.Id);
        await _wiper.WipeTenantDataAsync(tenant.Id, ct);
        return await _seed.SeedAsync(tenant.Id, ct);
    }

    private static bool IsDemoSlug(string slug) =>
        !string.IsNullOrWhiteSpace(slug) &&
        slug.StartsWith(SlugPrefix, StringComparison.OrdinalIgnoreCase);
}
