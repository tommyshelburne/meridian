using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class AuditLogRepository : IAuditLog
{
    private readonly MeridianDbContext _db;

    public AuditLogRepository(MeridianDbContext db) => _db = db;

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken ct)
    {
        await _db.AuditEvents.AddAsync(auditEvent, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(
        Guid tenantId,
        string? entityType,
        string? eventType,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken ct)
    {
        var query = _db.AuditEvents.AsQueryable();

        if (entityType is not null)
            query = query.Where(e => e.EntityType == entityType);
        if (eventType is not null)
            query = query.Where(e => e.EventType == eventType);
        if (from.HasValue)
            query = query.Where(e => e.OccurredAt >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.OccurredAt <= to.Value);

        return await query
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
