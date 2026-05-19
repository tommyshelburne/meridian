namespace Meridian.Domain.Sources;

/// <summary>
/// A webhook payload received for an <see cref="SourceDefinition"/> of type
/// <see cref="SourceAdapterType.InboundWebhook"/>, persisted until the ingestion
/// run drains it. Persistence is what lets the Portal process (which receives
/// the HTTP POST) hand the payload to the Worker process (which runs ingestion).
/// </summary>
public class WebhookPayloadRecord
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceDefinitionId { get; private set; }
    public string RawJson { get; private set; } = null!;
    public DateTimeOffset ReceivedAt { get; private set; }

    private WebhookPayloadRecord() { }

    public static WebhookPayloadRecord Receive(
        Guid tenantId, Guid sourceDefinitionId, string rawJson, DateTimeOffset receivedAt)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (sourceDefinitionId == Guid.Empty)
            throw new ArgumentException("SourceDefinitionId is required.", nameof(sourceDefinitionId));
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new ArgumentException("Payload JSON is required.", nameof(rawJson));

        return new WebhookPayloadRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceDefinitionId = sourceDefinitionId,
            RawJson = rawJson,
            ReceivedAt = receivedAt
        };
    }
}
