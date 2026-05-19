namespace Meridian.Application.Ports;

public record WebhookPayload(
    Guid TenantId,
    Guid SourceDefinitionId,
    string RawJson,
    DateTimeOffset ReceivedAt);

public interface IWebhookIngestQueue
{
    Task EnqueueAsync(WebhookPayload payload, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookPayload>> DrainForSourceAsync(
        Guid sourceDefinitionId, CancellationToken ct = default);
}
