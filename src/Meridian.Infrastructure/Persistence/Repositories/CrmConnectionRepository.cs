using Meridian.Application.Ports;
using Meridian.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class CrmConnectionRepository : ICrmConnectionRepository
{
    private readonly MeridianDbContext _db;

    public CrmConnectionRepository(MeridianDbContext db) => _db = db;

    public Task<CrmConnection?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
        => _db.CrmConnections.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public Task<CrmConnection?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.CrmConnections.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(CrmConnection connection, CancellationToken ct)
        => await _db.CrmConnections.AddAsync(connection, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
