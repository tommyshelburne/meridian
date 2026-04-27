using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Crm.Pipedrive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class PipedriveAdapterTests
{
    private const string BaseUrl = "https://api.pipedrive.test/v1/";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly CrmConnectionContext PipedriveCtx = new(
        TenantId, CrmProvider.Pipedrive, "tok-123", null, null, null, null);

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static PipedriveAdapter CreateAdapter(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new FakeHandler(handler))
        {
            BaseAddress = new Uri(BaseUrl)
        };
        var opts = Options.Create(new PipedriveOptions { BaseUrl = BaseUrl });
        return new PipedriveAdapter(http, opts, NullLogger<PipedriveAdapter>.Instance);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        // Use camelCase so [JsonPropertyName] on the wire DTOs lines up with the
        // typed deserializer regardless of the writing object's property casing.
        var content = JsonSerializer.Serialize(body, WriteOptions);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static object Envelope(object? data, bool success = true, string? error = null) =>
        new { success, error, data };

    private static Opportunity SampleOpportunity(decimal? value = 100_000m) =>
        Opportunity.Create(
            tenantId: TenantId,
            externalId: "EXT-1",
            source: OpportunitySource.SamGov,
            title: "Contact center services",
            description: "desc",
            agency: Meridian.Domain.Common.Agency.Create("VA", AgencyType.FederalCivilian, null),
            postedDate: DateTimeOffset.UtcNow.AddDays(-1),
            responseDeadline: DateTimeOffset.UtcNow.AddDays(14),
            naicsCode: "561422",
            estimatedValue: value,
            procurementVehicle: null,
            sourceDefinitionId: null);

    [Fact]
    public void Provider_is_Pipedrive()
    {
        var adapter = CreateAdapter(_ => Json(Envelope(new { items = new object[0] })));
        adapter.Provider.Should().Be(CrmProvider.Pipedrive);
    }

    [Fact]
    public async Task FindOrCreateOrganization_returns_existing_match()
    {
        var adapter = CreateAdapter(req =>
        {
            req.Method.Should().Be(HttpMethod.Get);
            req.RequestUri!.PathAndQuery.Should().StartWith("/v1/organizations/search");
            req.RequestUri.Query.Should().Contain("api_token=tok-123");
            return Json(Envelope(new
            {
                items = new[]
                {
                    new { item = new { id = 42, name = "VA" } }
                }
            }));
        });

        var result = await adapter.FindOrCreateOrganizationAsync(PipedriveCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("42");
    }

    [Fact]
    public async Task FindOrCreateOrganization_creates_when_search_returns_nothing()
    {
        var calls = 0;
        var adapter = CreateAdapter(req =>
        {
            calls++;
            if (calls == 1)
            {
                req.RequestUri!.PathAndQuery.Should().StartWith("/v1/organizations/search");
                return Json(Envelope(new { items = new object[0] }));
            }
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.PathAndQuery.Should().StartWith("/v1/organizations");
            return Json(Envelope(new { id = 99, name = "VA" }));
        });

        var result = await adapter.FindOrCreateOrganizationAsync(PipedriveCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("99");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task FindOrCreateOrganization_rejects_blank_name()
    {
        var adapter = CreateAdapter(_ => throw new InvalidOperationException("HTTP should not be called"));
        var result = await adapter.FindOrCreateOrganizationAsync(PipedriveCtx, "  ", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task FindOrCreateOrganization_returns_failure_on_pipedrive_error_envelope()
    {
        var adapter = CreateAdapter(_ => Json(Envelope(data: null, success: false, error: "rate limited")));

        var result = await adapter.FindOrCreateOrganizationAsync(PipedriveCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("rate limited");
    }

    [Fact]
    public async Task FindOrCreateOrganization_returns_failure_on_401()
    {
        var adapter = CreateAdapter(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}")
        });

        var result = await adapter.FindOrCreateOrganizationAsync(PipedriveCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("401");
    }

    [Fact]
    public async Task CreateDeal_posts_required_fields_and_returns_id()
    {
        var bodyCaptured = string.Empty;
        var adapter = CreateAdapter(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.PathAndQuery.Should().StartWith("/v1/deals");
            bodyCaptured = req.Content!.ReadAsStringAsync().Result;
            return Json(Envelope(new { id = 555 }));
        });

        var result = await adapter.CreateDealAsync(PipedriveCtx, SampleOpportunity(), "42", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("555");
        bodyCaptured.Should().Contain("\"title\":\"Contact center services\"");
        bodyCaptured.Should().Contain("\"org_id\":42");
        bodyCaptured.Should().Contain("\"value\":100000");
        bodyCaptured.Should().Contain("\"currency\":\"USD\"");
    }

    [Fact]
    public async Task CreateDeal_includes_pipeline_id_when_default_is_set_and_numeric()
    {
        var bodyCaptured = string.Empty;
        var ctx = PipedriveCtx with { DefaultPipelineId = "7" };
        var adapter = CreateAdapter(req =>
        {
            bodyCaptured = req.Content!.ReadAsStringAsync().Result;
            return Json(Envelope(new { id = 1 }));
        });

        await adapter.CreateDealAsync(ctx, SampleOpportunity(), "42", CancellationToken.None);

        bodyCaptured.Should().Contain("\"pipeline_id\":7");
    }

    [Fact]
    public async Task CreateDeal_omits_pipeline_id_when_default_not_numeric()
    {
        var bodyCaptured = string.Empty;
        var ctx = PipedriveCtx with { DefaultPipelineId = "main" };
        var adapter = CreateAdapter(req =>
        {
            bodyCaptured = req.Content!.ReadAsStringAsync().Result;
            return Json(Envelope(new { id = 1 }));
        });

        await adapter.CreateDealAsync(ctx, SampleOpportunity(), "42", CancellationToken.None);

        bodyCaptured.Should().NotContain("pipeline_id");
    }

    [Fact]
    public async Task CreateDeal_rejects_non_numeric_org_id_without_calling_pipedrive()
    {
        var adapter = CreateAdapter(_ => throw new InvalidOperationException("HTTP should not be called"));
        var result = await adapter.CreateDealAsync(PipedriveCtx, SampleOpportunity(), "noop-org:va", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateDealStage_sends_put_with_stage_id()
    {
        var bodyCaptured = string.Empty;
        var adapter = CreateAdapter(req =>
        {
            req.Method.Should().Be(HttpMethod.Put);
            req.RequestUri!.PathAndQuery.Should().StartWith("/v1/deals/555");
            bodyCaptured = req.Content!.ReadAsStringAsync().Result;
            return Json(Envelope(new { id = 555 }));
        });

        var result = await adapter.UpdateDealStageAsync(PipedriveCtx, "555", "4", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bodyCaptured.Should().Contain("\"stage_id\":4");
    }

    [Fact]
    public async Task AddActivity_posts_deal_id_type_and_subject()
    {
        var bodyCaptured = string.Empty;
        var adapter = CreateAdapter(req =>
        {
            req.RequestUri!.PathAndQuery.Should().StartWith("/v1/activities");
            bodyCaptured = req.Content!.ReadAsStringAsync().Result;
            return Json(Envelope(new { id = 9 }));
        });

        var result = await adapter.AddActivityAsync(PipedriveCtx, "555", "call", "Followup outreach", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bodyCaptured.Should().Contain("\"deal_id\":555");
        bodyCaptured.Should().Contain("\"type\":\"call\"");
        bodyCaptured.Should().Contain("\"subject\":\"Followup outreach\"");
    }

    [Fact]
    public async Task AddActivity_defaults_type_when_blank()
    {
        var bodyCaptured = string.Empty;
        var adapter = CreateAdapter(req =>
        {
            bodyCaptured = req.Content!.ReadAsStringAsync().Result;
            return Json(Envelope(new { id = 9 }));
        });

        await adapter.AddActivityAsync(PipedriveCtx, "555", "  ", "Note", CancellationToken.None);

        bodyCaptured.Should().Contain("\"type\":\"task\"");
    }
}
