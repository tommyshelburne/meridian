namespace Meridian.Application.Ports;

public record WebhookPayload(
    Guid TenantId,
    Guid SourceDefinitionId,
    string RawJson,
    DateTimeOffset ReceivedAt);

public interface IWebhookIngestQueue
{
    void Enqueue(WebhookPayload payload);
    IReadOnlyList<WebhookPayload> DrainForSource(Guid sourceDefinitionId);
}
