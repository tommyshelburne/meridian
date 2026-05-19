using FluentAssertions;
using Meridian.Application.Ports;
using Meridian.Infrastructure.Ingestion.Generic;

namespace Meridian.Integration;

/// <summary>
/// Covers <see cref="DbWebhookIngestQueue"/> — the durable webhook queue that
/// lets a payload POSTed in the Portal process be drained by the ingestion run
/// in the Worker process. Each test enqueues and drains through SEPARATE
/// <c>MeridianDbContext</c> instances to mirror the cross-process split.
/// </summary>
public class WebhookIngestQueuePersistenceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Enqueue_persists_payload_visible_to_a_separate_context()
    {
        using var fx = new IntegrationTestFixture();
        var sourceId = Guid.NewGuid();

        // Enqueue via one context (the Portal process)...
        await using (var db = fx.NewDbContext())
        {
            await new DbWebhookIngestQueue(db).EnqueueAsync(
                new WebhookPayload(TenantId, sourceId, """{"id":"W-1"}""", DateTimeOffset.UtcNow));
        }

        // ...drain via a separate context (the Worker process).
        await using (var db = fx.NewDbContext())
        {
            var drained = await new DbWebhookIngestQueue(db).DrainForSourceAsync(sourceId);

            drained.Should().ContainSingle();
            drained[0].RawJson.Should().Be("""{"id":"W-1"}""");
            drained[0].TenantId.Should().Be(TenantId);
            drained[0].SourceDefinitionId.Should().Be(sourceId);
        }
    }

    [Fact]
    public async Task DrainForSource_removes_drained_payloads()
    {
        using var fx = new IntegrationTestFixture();
        var sourceId = Guid.NewGuid();

        await using (var db = fx.NewDbContext())
        {
            await new DbWebhookIngestQueue(db).EnqueueAsync(
                new WebhookPayload(TenantId, sourceId, """{"id":"W-1"}""", DateTimeOffset.UtcNow));
        }

        await using (var db = fx.NewDbContext())
        {
            var first = await new DbWebhookIngestQueue(db).DrainForSourceAsync(sourceId);
            first.Should().ContainSingle();
        }

        await using (var db = fx.NewDbContext())
        {
            var second = await new DbWebhookIngestQueue(db).DrainForSourceAsync(sourceId);
            second.Should().BeEmpty("a drained payload must not be returned a second time");
        }
    }

    [Fact]
    public async Task DrainForSource_isolates_payloads_by_source()
    {
        using var fx = new IntegrationTestFixture();
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();

        await using (var db = fx.NewDbContext())
        {
            var queue = new DbWebhookIngestQueue(db);
            await queue.EnqueueAsync(new WebhookPayload(TenantId, sourceA, """{"id":"A"}""", DateTimeOffset.UtcNow));
            await queue.EnqueueAsync(new WebhookPayload(TenantId, sourceB, """{"id":"B"}""", DateTimeOffset.UtcNow));
        }

        await using (var db = fx.NewDbContext())
        {
            var drainedA = await new DbWebhookIngestQueue(db).DrainForSourceAsync(sourceA);
            drainedA.Should().ContainSingle();
            drainedA[0].RawJson.Should().Contain("\"A\"");
        }

        await using (var db = fx.NewDbContext())
        {
            var drainedB = await new DbWebhookIngestQueue(db).DrainForSourceAsync(sourceB);
            drainedB.Should().ContainSingle();
            drainedB[0].RawJson.Should().Contain("\"B\"");
        }
    }

    [Fact]
    public async Task DrainForSource_returns_empty_when_nothing_queued()
    {
        using var fx = new IntegrationTestFixture();

        await using var db = fx.NewDbContext();
        var drained = await new DbWebhookIngestQueue(db).DrainForSourceAsync(Guid.NewGuid());

        drained.Should().BeEmpty();
    }

    [Fact]
    public async Task DrainForSource_returns_payloads_oldest_first()
    {
        using var fx = new IntegrationTestFixture();
        var sourceId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        await using (var db = fx.NewDbContext())
        {
            var queue = new DbWebhookIngestQueue(db);
            await queue.EnqueueAsync(new WebhookPayload(TenantId, sourceId, """{"n":2}""", t0.AddSeconds(2)));
            await queue.EnqueueAsync(new WebhookPayload(TenantId, sourceId, """{"n":1}""", t0.AddSeconds(1)));
        }

        await using (var db = fx.NewDbContext())
        {
            var drained = await new DbWebhookIngestQueue(db).DrainForSourceAsync(sourceId);
            drained.Select(p => p.RawJson).Should().ContainInOrder("""{"n":1}""", """{"n":2}""");
        }
    }
}
