using Meridian.Domain.Audit;

namespace Meridian.Application.Ports;

public interface IAuditLog
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken ct);
    Task<IReadOnlyList<AuditEvent>> QueryAsync(Guid tenantId, string? entityType, string? eventType,
        DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken ct);
}
