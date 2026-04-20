using Meridian.Application.Ports;
using Meridian.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class UserTenantRepository : IUserTenantRepository
{
    private readonly MeridianDbContext _db;

    public UserTenantRepository(MeridianDbContext db) => _db = db;

    public Task<UserTenant?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.UserTenants.FirstOrDefaultAsync(ut => ut.Id == id, ct);

    public Task<UserTenant?> GetAsync(Guid userId, Guid tenantId, CancellationToken ct)
        => _db.UserTenants.FirstOrDefaultAsync(ut => ut.UserId == userId && ut.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<UserTenant>> GetForUserAsync(Guid userId, CancellationToken ct)
        => await _db.UserTenants
            .Where(ut => ut.UserId == userId && ut.Status != MembershipStatus.Removed)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserTenant>> GetForTenantAsync(Guid tenantId, CancellationToken ct)
        => await _db.UserTenants
            .Where(ut => ut.TenantId == tenantId && ut.Status != MembershipStatus.Removed)
            .ToListAsync(ct);

    public async Task AddAsync(UserTenant membership, CancellationToken ct)
        => await _db.UserTenants.AddAsync(membership, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
