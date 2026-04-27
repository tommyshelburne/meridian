using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Crm.HubSpot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class HubSpotAdapterTests
{
    private const string BaseUrl = "https://api.hubapi.test/";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly CrmConnectionContext HubSpotCtx = new(
        TenantId, CrmProvider.HubSpot, "tok-bearer", null, null, null, null);

    private record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private static (HubSpotAdapter adapter, List<CapturedRequest> log) CreateAdapter(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var log = new List<CapturedRequest>();
        var http = new HttpClient(new FakeHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            log.Add(new CapturedRequest(
                req.Method, req.RequestUri!,
                req.Headers.Authorization is null
                    ? null
                    : $"{req.Headers.Authorization.Scheme} {req.Headers.Authorization.Parameter}",
                body));
            return handler(req);
        }));
        var opts = Options.Create(new HubSpotOptions { BaseUrl = BaseUrl });
        return (new HubSpotAdapter(http, opts, NullLogger<HubSpotAdapter>.Instance), log);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = JsonSerializer.Serialize(body);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static Opportunity SampleOpportunity(decimal? value = 100_000m) =>
        Opportunity.Create(
            tenantId: TenantId,
            externalId: "EXT-1",
            source: OpportunitySource.SamGov,
            title: "Contact center services",
            description: "desc",
            agency: Agency.Create("VA", AgencyType.FederalCivilian, null),
            postedDate: DateTimeOffset.UtcNow.AddDays(-1),
            responseDeadline: DateTimeOffset.UtcNow.AddDays(14),
            naicsCode: "561422",
            estimatedValue: value,
            procurementVehicle: null,
            sourceDefinitionId: null);

    [Fact]
    public void Provider_is_HubSpot()
    {
        var (adapter, _) = CreateAdapter(_ => Json(new { results = new object[0] }));
        adapter.Provider.Should().Be(CrmProvider.HubSpot);
    }

    [Fact]
    public async Task FindOrCreateOrganization_returns_search_match()
    {
        var (adapter, log) = CreateAdapter(_ => Json(new
        {
            total = 1,
            results = new[] { new { id = "company-42", properties = new { name = "VA" } } }
        }));

        var result = await adapter.FindOrCreateOrganizationAsync(HubSpotCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("company-42");
        log.Should().ContainSingle();
        log[0].Uri.AbsolutePath.Should().Be("/crm/v3/objects/companies/search");
        log[0].Authorization.Should().Be("Bearer tok-bearer");
        log[0].Body.Should().Contain("\"value\":\"VA\"");
    }

    [Fact]
    public async Task FindOrCreateOrganization_creates_when_search_empty()
    {
        var calls = 0;
        var (adapter, log) = CreateAdapter(req =>
        {
            calls++;
            if (calls == 1) return Json(new { total = 0, results = new object[0] });
            return Json(new { id = "company-99", properties = new { name = "VA" } });
        });

        var result = await adapter.FindOrCreateOrganizationAsync(HubSpotCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("company-99");
        log.Should().HaveCount(2);
        log[1].Uri.AbsolutePath.Should().Be("/crm/v3/objects/companies");
        log[1].Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task CreateDeal_includes_amount_pipeline_and_company_association()
    {
        var ctx = HubSpotCtx with { DefaultPipelineId = "pipe-1" };
        var (adapter, log) = CreateAdapter(_ => Json(new { id = "deal-7", properties = new { } }));

        var result = await adapter.CreateDealAsync(ctx, SampleOpportunity(), "company-42", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("deal-7");
        var body = log[0].Body;
        body.Should().Contain("\"dealname\":\"Contact center services\"");
        body.Should().Contain("\"amount\":\"100000\"");
        body.Should().Contain("\"pipeline\":\"pipe-1\"");
        body.Should().Contain("\"associationTypeId\":5");
        body.Should().Contain("\"id\":\"company-42\"");
    }

    [Fact]
    public async Task CreateDeal_omits_amount_when_estimated_value_null()
    {
        var (adapter, log) = CreateAdapter(_ => Json(new { id = "deal-8", properties = new { } }));

        await adapter.CreateDealAsync(HubSpotCtx, SampleOpportunity(value: null), "company-42", CancellationToken.None);

        log[0].Body.Should().NotContain("amount");
    }

    [Fact]
    public async Task UpdateDealStage_sends_patch_with_dealstage_property()
    {
        var (adapter, log) = CreateAdapter(_ => Json(new { id = "deal-7", properties = new { } }));

        var result = await adapter.UpdateDealStageAsync(HubSpotCtx, "deal-7", "qualifiedtobuy", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        log[0].Method.Should().Be(HttpMethod.Patch);
        log[0].Uri.AbsolutePath.Should().Be("/crm/v3/objects/deals/deal-7");
        log[0].Body.Should().Contain("\"dealstage\":\"qualifiedtobuy\"");
    }

    [Fact]
    public async Task AddActivity_defaults_to_note_with_deal_association()
    {
        var (adapter, log) = CreateAdapter(_ => Json(new { id = "note-1", properties = new { } }));

        var result = await adapter.AddActivityAsync(HubSpotCtx, "deal-7", "", "Followup logged", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        log[0].Uri.AbsolutePath.Should().Be("/crm/v3/objects/notes");
        log[0].Body.Should().Contain("\"hs_note_body\":\"Followup logged\"");
        log[0].Body.Should().Contain("\"associationTypeId\":214");
    }

    [Fact]
    public async Task AddActivity_routes_task_type_to_tasks_endpoint()
    {
        var (adapter, log) = CreateAdapter(_ => Json(new { id = "task-1", properties = new { } }));

        await adapter.AddActivityAsync(HubSpotCtx, "deal-7", "task", "Call back next week", CancellationToken.None);

        log[0].Uri.AbsolutePath.Should().Be("/crm/v3/objects/tasks");
        log[0].Body.Should().Contain("\"hs_task_subject\":\"Call back next week\"");
        log[0].Body.Should().Contain("\"associationTypeId\":216");
    }

    [Fact]
    public async Task AddActivity_routes_call_type_to_calls_endpoint()
    {
        var (adapter, log) = CreateAdapter(_ => Json(new { id = "call-1", properties = new { } }));

        await adapter.AddActivityAsync(HubSpotCtx, "deal-7", "call", "30-min discovery", CancellationToken.None);

        log[0].Uri.AbsolutePath.Should().Be("/crm/v3/objects/calls");
        log[0].Body.Should().Contain("\"associationTypeId\":206");
    }

    [Fact]
    public async Task SendAsync_surfaces_hubspot_error_message_on_4xx()
    {
        var (adapter, _) = CreateAdapter(_ => Json(new
        {
            status = "error",
            message = "Property \"name\" does not exist",
            category = "VALIDATION_ERROR"
        }, HttpStatusCode.BadRequest));

        var result = await adapter.FindOrCreateOrganizationAsync(HubSpotCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Property \"name\" does not exist");
    }

    [Fact]
    public async Task SendAsync_fails_when_auth_token_blank()
    {
        var (adapter, _) = CreateAdapter(_ => throw new InvalidOperationException("HTTP should not be called"));
        var anonCtx = HubSpotCtx with { AuthToken = "" };

        var result = await adapter.FindOrCreateOrganizationAsync(anonCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }
}
