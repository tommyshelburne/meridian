using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion.Generic;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure;

public class InboundWebhookAdapterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static SourceDefinition CreateSource(object parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        return SourceDefinition.Create(TenantId, SourceAdapterType.InboundWebhook, "Test Webhook", json);
    }

    private static object DefaultParameters() => new
    {
        secret = "s3cret",
        agencyName = "Test Agency",
        fieldMap = new { externalId = "id", title = "title", postedDate = "posted", naicsCode = "naics" }
    };

    [Fact]
    public async Task Drains_queue_and_maps_single_object()
    {
        var queue = new InMemoryWebhookIngestQueue();
        var source = CreateSource(DefaultParameters());
        queue.Enqueue(new WebhookPayload(TenantId, source.Id,
            """{"id":"W-1","title":"Webhook RFP","naics":"561422"}""",
            DateTimeOffset.UtcNow));

        var adapter = new InboundWebhookAdapter(queue, NullLogger<InboundWebhookAdapter>.Instance);

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].ExternalId.Should().Be("W-1");
        result.Value[0].Title.Should().Be("Webhook RFP");
        result.Value[0].NaicsCode.Should().Be("561422");
    }

    [Fact]
    public async Task Maps_batched_array_payload()
    {
        var queue = new InMemoryWebhookIngestQueue();
        var source = CreateSource(DefaultParameters());
        queue.Enqueue(new WebhookPayload(TenantId, source.Id,
            """[{"id":"W-1","title":"First"},{"id":"W-2","title":"Second"}]""",
            DateTimeOffset.UtcNow));

        var adapter = new InboundWebhookAdapter(queue, NullLogger<InboundWebhookAdapter>.Instance);

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value.Should().HaveCount(2);
        result.Value!.Select(o => o.ExternalId).Should().Contain(new[] { "W-1", "W-2" });
    }

    [Fact]
    public async Task Queue_is_drained_after_fetch()
    {
        var queue = new InMemoryWebhookIngestQueue();
        var source = CreateSource(DefaultParameters());
        queue.Enqueue(new WebhookPayload(TenantId, source.Id,
            """{"id":"W-1","title":"First"}""",
            DateTimeOffset.UtcNow));

        var adapter = new InboundWebhookAdapter(queue, NullLogger<InboundWebhookAdapter>.Instance);

        var first = await adapter.FetchAsync(source, CancellationToken.None);
        var second = await adapter.FetchAsync(source, CancellationToken.None);

        first.Value.Should().HaveCount(1);
        second.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_malformed_payloads_without_failing_run()
    {
        var queue = new InMemoryWebhookIngestQueue();
        var source = CreateSource(DefaultParameters());
        queue.Enqueue(new WebhookPayload(TenantId, source.Id, "not json at all", DateTimeOffset.UtcNow));
        queue.Enqueue(new WebhookPayload(TenantId, source.Id,
            """{"id":"W-Good","title":"OK"}""",
            DateTimeOffset.UtcNow));

        var adapter = new InboundWebhookAdapter(queue, NullLogger<InboundWebhookAdapter>.Instance);

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].ExternalId.Should().Be("W-Good");
    }

    [Fact]
    public async Task Fails_when_parameters_missing()
    {
        var queue = new InMemoryWebhookIngestQueue();
        var source = SourceDefinition.Create(
            TenantId, SourceAdapterType.InboundWebhook, "Test", "{}");
        var adapter = new InboundWebhookAdapter(queue, NullLogger<InboundWebhookAdapter>.Instance);

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("secret");
    }

    [Fact]
    public void Queue_isolates_payloads_by_source_id()
    {
        var queue = new InMemoryWebhookIngestQueue();
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();

        queue.Enqueue(new WebhookPayload(TenantId, sourceA, """{"id":"A"}""", DateTimeOffset.UtcNow));
        queue.Enqueue(new WebhookPayload(TenantId, sourceB, """{"id":"B"}""", DateTimeOffset.UtcNow));

        var drainedA = queue.DrainForSource(sourceA);
        var drainedB = queue.DrainForSource(sourceB);

        drainedA.Should().HaveCount(1);
        drainedA[0].RawJson.Should().Contain("\"A\"");
        drainedB.Should().HaveCount(1);
        drainedB[0].RawJson.Should().Contain("\"B\"");
    }
}
