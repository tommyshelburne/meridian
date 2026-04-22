using System.Collections.Concurrent;
using Meridian.Application.Ports;

namespace Meridian.Infrastructure.Ingestion.Generic;

public class InMemoryWebhookIngestQueue : IWebhookIngestQueue
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<WebhookPayload>> _queues = new();

    public void Enqueue(WebhookPayload payload)
    {
        var queue = _queues.GetOrAdd(payload.SourceDefinitionId, _ => new ConcurrentQueue<WebhookPayload>());
        queue.Enqueue(payload);
    }

    public IReadOnlyList<WebhookPayload> DrainForSource(Guid sourceDefinitionId)
    {
        if (!_queues.TryGetValue(sourceDefinitionId, out var queue))
            return Array.Empty<WebhookPayload>();

        var drained = new List<WebhookPayload>();
        while (queue.TryDequeue(out var payload))
            drained.Add(payload);
        return drained;
    }
}
