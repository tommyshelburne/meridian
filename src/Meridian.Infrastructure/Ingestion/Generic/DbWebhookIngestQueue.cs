using Meridian.Application.Ports;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Ingestion.Generic;

/// <summary>
/// Database-backed <see cref="IWebhookIngestQueue"/>. The webhook POST endpoint
/// runs in the Portal process while the ingestion run that drains the queue runs
/// in the Worker process, so the queue must be durable shared state — a
/// per-process in-memory queue is invisible across the two processes and silently
/// drops every payload in the split deployment.
/// </summary>
public class DbWebhookIngestQueue : IWebhookIngestQueue
{
    private readonly MeridianDbContext _db;

    public DbWebhookIngestQueue(MeridianDbContext db) => _db = db;

    public async Task EnqueueAsync(WebhookPayload payload, CancellationToken ct = default)
    {
        _db.WebhookPayloads.Add(WebhookPayloadRecord.Receive(
            payload.TenantId, payload.SourceDefinitionId, payload.RawJson, payload.ReceivedAt));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookPayload>> DrainForSourceAsync(
        Guid sourceDefinitionId, CancellationToken ct = default)
    {
        var rows = await _db.WebhookPayloads
            .Where(p => p.SourceDefinitionId == sourceDefinitionId)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return Array.Empty<WebhookPayload>();

        // Read-then-remove: the ingestion run is the sole drainer and runs
        // serially per source, so there is no competing reader to race.
        _db.WebhookPayloads.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);

        // Order client-side: the SQLite provider can't translate a
        // DateTimeOffset ORDER BY, and a drain reads the whole (small) batch.
        return rows
            .OrderBy(r => r.ReceivedAt)
            .Select(r => new WebhookPayload(r.TenantId, r.SourceDefinitionId, r.RawJson, r.ReceivedAt))
            .ToList();
    }
}
