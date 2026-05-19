using System.Collections.Concurrent;
using Meridian.Application.Ports;

namespace Meridian.Infrastructure.Ingestion.Generic;

/// <summary>
/// In-memory <see cref="IWebhookIngestQueue"/>. Only valid when the webhook POST
/// endpoint and the ingestion run share a process (single-process hosting, tests).
/// The production Portal/Worker split uses <see cref="DbWebhookIngestQueue"/>.
/// </summary>
public class InMemoryWebhookIngestQueue : IWebhookIngestQueue
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<WebhookPayload>> _queues = new();

    public Task EnqueueAsync(WebhookPayload payload, CancellationToken ct = default)
    {
        var queue = _queues.GetOrAdd(payload.SourceDefinitionId, _ => new ConcurrentQueue<WebhookPayload>());
        queue.Enqueue(payload);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WebhookPayload>> DrainForSourceAsync(
        Guid sourceDefinitionId, CancellationToken ct = default)
    {
        if (!_queues.TryGetValue(sourceDefinitionId, out var queue))
            return Task.FromResult<IReadOnlyList<WebhookPayload>>(Array.Empty<WebhookPayload>());

        var drained = new List<WebhookPayload>();
        while (queue.TryDequeue(out var payload))
            drained.Add(payload);
        return Task.FromResult<IReadOnlyList<WebhookPayload>>(drained);
    }
}
