using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Crm.Salesforce;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class SalesforceAdapterTests
{
    private const string InstanceBase = "https://acme.my.salesforce.test/services/data/v59.0/";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly CrmConnectionContext SfCtx = new(
        TenantId, CrmProvider.Salesforce, "tok-bearer", null, null, InstanceBase, null);

    private record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private static (SalesforceAdapter adapter, List<CapturedRequest> log) Create(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        SalesforceOptions? overrideOptions = null)
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
        var opts = Options.Create(overrideOptions ?? new SalesforceOptions
        {
            ApiVersion = "v59.0",
            DefaultStageName = "Prospecting",
            DefaultCloseDateDays = 30
        });
        return (new SalesforceAdapter(http, opts, NullLogger<SalesforceAdapter>.Instance), log);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    private static Opportunity SampleOpportunity(decimal? value = 100_000m, DateTimeOffset? deadline = null) =>
        Opportunity.Create(
            tenantId: TenantId,
            externalId: "EXT-1",
            source: OpportunitySource.SamGov,
            title: "Contact center services",
            description: "desc",
            agency: Agency.Create("VA", AgencyType.FederalCivilian, null),
            postedDate: DateTimeOffset.UtcNow.AddDays(-1),
            responseDeadline: deadline,
            naicsCode: "561422",
            estimatedValue: value,
            procurementVehicle: null,
            sourceDefinitionId: null);

    [Fact]
    public void Provider_is_Salesforce()
    {
        var (adapter, _) = Create(_ => Json(new { totalSize = 0, done = true, records = new object[0] }));
        adapter.Provider.Should().Be(CrmProvider.Salesforce);
    }

    [Fact]
    public async Task FindOrCreateOrganization_returns_match_from_query()
    {
        var (adapter, log) = Create(_ => Json(new
        {
            totalSize = 1,
            done = true,
            records = new[] { new { Id = "001ABCDEF" } }
        }));

        var result = await adapter.FindOrCreateOrganizationAsync(SfCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("001ABCDEF");
        log[0].Method.Should().Be(HttpMethod.Get);
        log[0].Uri.AbsolutePath.Should().Be("/services/data/v59.0/query");
        Uri.UnescapeDataString(log[0].Uri.Query).Should().Contain("Name = 'VA'");
        log[0].Authorization.Should().Be("Bearer tok-bearer");
    }

    [Fact]
    public async Task FindOrCreateOrganization_creates_when_query_empty()
    {
        var calls = 0;
        var (adapter, log) = Create(req =>
        {
            calls++;
            return calls == 1
                ? Json(new { totalSize = 0, done = true, records = new object[0] })
                : Json(new { id = "001NEWACCT", success = true, errors = new object[0] });
        });

        var result = await adapter.FindOrCreateOrganizationAsync(SfCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("001NEWACCT");
        log.Should().HaveCount(2);
        log[1].Method.Should().Be(HttpMethod.Post);
        log[1].Uri.AbsolutePath.Should().Be("/services/data/v59.0/sobjects/Account/");
        log[1].Body.Should().Contain("\"Name\":\"VA\"");
    }

    [Fact]
    public async Task FindOrCreateOrganization_escapes_soql_quotes_in_search_term()
    {
        var (adapter, log) = Create(_ => Json(new { totalSize = 0, done = true, records = new object[0] }));
        // POST will fail when search returns empty — we only care about the GET shape here.
        var calls = 0;
        var (adapter2, log2) = Create(req =>
        {
            calls++;
            return calls == 1
                ? Json(new { totalSize = 0, done = true, records = new object[0] })
                : Json(new { id = "001", success = true, errors = new object[0] });
        });

        await adapter2.FindOrCreateOrganizationAsync(SfCtx, "Smith's Co", CancellationToken.None);

        Uri.UnescapeDataString(log2[0].Uri.Query).Should().Contain("Name = 'Smith\\'s Co'");
    }

    [Fact]
    public async Task CreateDeal_uses_response_deadline_for_close_date_when_present()
    {
        var (adapter, log) = Create(_ => Json(new { id = "006OPPID", success = true, errors = new object[0] }));
        var deadline = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var result = await adapter.CreateDealAsync(SfCtx, SampleOpportunity(deadline: deadline), "001ABCDEF", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("006OPPID");
        log[0].Body.Should().Contain("\"Name\":\"Contact center services\"");
        log[0].Body.Should().Contain("\"AccountId\":\"001ABCDEF\"");
        log[0].Body.Should().Contain("\"StageName\":\"Prospecting\"");
        log[0].Body.Should().Contain("\"CloseDate\":\"2026-06-15\"");
        log[0].Body.Should().Contain("\"Amount\":100000");
    }

    [Fact]
    public async Task CreateDeal_falls_back_to_default_close_date_when_deadline_missing()
    {
        var (adapter, log) = Create(_ => Json(new { id = "006X", success = true, errors = new object[0] }));

        await adapter.CreateDealAsync(SfCtx, SampleOpportunity(deadline: null), "001A", CancellationToken.None);

        var expected = DateTimeOffset.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        log[0].Body.Should().Contain($"\"CloseDate\":\"{expected}\"");
    }

    [Fact]
    public async Task CreateDeal_omits_amount_when_estimated_value_null()
    {
        var (adapter, log) = Create(_ => Json(new { id = "006Y", success = true, errors = new object[0] }));

        await adapter.CreateDealAsync(SfCtx, SampleOpportunity(value: null), "001A", CancellationToken.None);

        log[0].Body.Should().NotContain("Amount");
    }

    [Fact]
    public async Task CreateDeal_unwraps_failure_envelope()
    {
        var (adapter, _) = Create(_ => Json(new
        {
            id = (string?)null,
            success = false,
            errors = new[]
            {
                new { statusCode = "FIELD_CUSTOM_VALIDATION_EXCEPTION", message = "Stage required" }
            }
        }));

        var result = await adapter.CreateDealAsync(SfCtx, SampleOpportunity(), "001A", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Stage required");
    }

    [Fact]
    public async Task UpdateDealStage_patches_with_stage_name_and_returns_ok_on_204()
    {
        var (adapter, log) = Create(_ => NoContent());

        var result = await adapter.UpdateDealStageAsync(SfCtx, "006XYZ", "Closed Won", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        log[0].Method.Should().Be(HttpMethod.Patch);
        log[0].Uri.AbsolutePath.Should().Be("/services/data/v59.0/sobjects/Opportunity/006XYZ");
        log[0].Body.Should().Contain("\"StageName\":\"Closed Won\"");
    }

    [Fact]
    public async Task AddActivity_creates_task_with_what_id_and_subtype()
    {
        var (adapter, log) = Create(_ => Json(new { id = "00TXYZ", success = true, errors = new object[0] }));

        var result = await adapter.AddActivityAsync(SfCtx, "006OPP", "call", "Discovery call", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        log[0].Uri.AbsolutePath.Should().Be("/services/data/v59.0/sobjects/Task/");
        log[0].Body.Should().Contain("\"WhatId\":\"006OPP\"");
        log[0].Body.Should().Contain("\"Subject\":\"Discovery call\"");
        log[0].Body.Should().Contain("\"TaskSubtype\":\"Call\"");
    }

    [Fact]
    public async Task AddActivity_truncates_subject_to_255_chars()
    {
        var (adapter, log) = Create(_ => Json(new { id = "00TY", success = true, errors = new object[0] }));
        var longText = new string('x', 400);

        await adapter.AddActivityAsync(SfCtx, "006OPP", "task", longText, CancellationToken.None);

        // Subject capped at 255; full text preserved in Description.
        log[0].Body.Should().Contain($"\"Subject\":\"{new string('x', 255)}\"");
        log[0].Body.Should().Contain($"\"Description\":\"{longText}\"");
    }

    [Fact]
    public async Task SendAsync_fails_fast_when_api_base_url_missing()
    {
        var ctxNoBase = SfCtx with { ApiBaseUrl = null };
        var (adapter, log) = Create(_ => throw new InvalidOperationException("HTTP should not be called"));

        var result = await adapter.FindOrCreateOrganizationAsync(ctxNoBase, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("ApiBaseUrl");
        log.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_surfaces_salesforce_error_array_on_4xx()
    {
        var (adapter, _) = Create(_ => Json(new[]
        {
            new { statusCode = "INVALID_SESSION_ID", message = "Session expired", fields = (string[]?)null }
        }, HttpStatusCode.Unauthorized));

        var result = await adapter.FindOrCreateOrganizationAsync(SfCtx, "VA", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("INVALID_SESSION_ID");
        result.Error.Should().Contain("Session expired");
    }
}
