using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Domain.Sources;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.E2E;

/// <summary>
/// Regression guard for the webhook ingest endpoint. An external caller has no
/// Meridian session cookie and therefore no tenant context, so the endpoint
/// must resolve the SourceDefinition WITHOUT the EF tenant query filter. Before
/// the fix the tenant-scoped lookup returned null and every unauthenticated POST
/// failed — the Inbound Webhook source type was unusable by real senders.
/// </summary>
public class WebhookIngestEndpointTests : IClassFixture<PortalFactory>
{
    private readonly PortalFactory _factory;

    public WebhookIngestEndpointTests(PortalFactory factory) => _factory = factory;

    private const string Secret = "e2e-webhook-secret";

    private static StringContent JsonBody() =>
        new("{\"id\":\"EXT-1\",\"title\":\"State RFP\"}", Encoding.UTF8, "application/json");

    private async Task<SourceDefinition> SeedWebhookSourceAsync(string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var tenant = Tenant.Create($"Workspace {slug}", slug);
        var parametersJson =
            $"{{\"secret\":\"{Secret}\",\"agencyName\":\"Webhook Partner\"," +
            "\"fieldMap\":{\"externalId\":\"id\",\"title\":\"title\"}}";
        var source = SourceDefinition.Create(
            tenant.Id, SourceAdapterType.InboundWebhook, "Partner webhook", parametersJson);
        db.Tenants.Add(tenant);
        db.SourceDefinitions.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    [Fact]
    public async Task External_post_with_no_session_cookie_is_accepted_and_enqueued()
    {
        var source = await SeedWebhookSourceAsync("wh-accept");

        var client = _factory.CreateClient(); // no auth header — a real external caller
        client.DefaultRequestHeaders.Add("X-Meridian-Secret", Secret);

        var response = await client.PostAsync($"/api/webhooks/ingest/{source.Id}", JsonBody());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "an external webhook POST has no tenant context — the endpoint must resolve the source unfiltered");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        (await db.WebhookPayloads.AnyAsync(p => p.SourceDefinitionId == source.Id))
            .Should().BeTrue("the accepted payload must be durably enqueued");
    }

    [Fact]
    public async Task External_post_with_a_wrong_secret_is_rejected()
    {
        var source = await SeedWebhookSourceAsync("wh-badsecret");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Secret", "wrong-secret");

        var response = await client.PostAsync($"/api/webhooks/ingest/{source.Id}", JsonBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the per-source secret still gates ingestion even though the source resolves cross-tenant");
    }

    [Fact]
    public async Task Post_to_an_unknown_source_is_not_found()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Secret", Secret);

        var response = await client.PostAsync($"/api/webhooks/ingest/{Guid.NewGuid()}", JsonBody());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
