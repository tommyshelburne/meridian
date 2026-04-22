using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion.Generic;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Unit.Infrastructure;

public class GenericRestAdapterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static GenericRestAdapter CreateAdapter(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHandler(handler));
        return new GenericRestAdapter(httpClient, NullLogger<GenericRestAdapter>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    private static SourceDefinition CreateSource(object parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        return SourceDefinition.Create(TenantId, SourceAdapterType.GenericRest, "Test REST", json);
    }

    [Fact]
    public async Task Maps_root_level_array_with_field_map()
    {
        var payload = """
            [
              { "id": "123", "name": "Contact Center RFP", "agency_posted": "2026-04-15", "naics": "561422", "value": 2500000 },
              { "id": "456", "name": "IT Support Contract", "agency_posted": "2026-04-16" }
            ]
            """;
        var adapter = CreateAdapter(_ => JsonResponse(payload));
        var source = CreateSource(new
        {
            url = "https://api.example.com/opps",
            agencyName = "Texas DIR",
            agencyState = "TX",
            resultsJsonPath = "$",
            fieldMap = new
            {
                externalId = "id",
                title = "name",
                postedDate = "agency_posted",
                naicsCode = "naics",
                estimatedValue = "value"
            }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].ExternalId.Should().Be("123");
        result.Value[0].Title.Should().Be("Contact Center RFP");
        result.Value[0].NaicsCode.Should().Be("561422");
        result.Value[0].EstimatedValue.Should().Be(2500000m);
        result.Value[0].AgencyName.Should().Be("Texas DIR");
        result.Value[0].AgencyState.Should().Be("TX");
        result.Value[0].AgencyType.Should().Be(AgencyType.StateLocal);
    }

    [Fact]
    public async Task Resolves_nested_json_path_to_results_array()
    {
        var payload = """
            {
              "data": {
                "items": [
                  { "opportunity_id": "N-001", "heading": "Nested RFP" }
                ]
              }
            }
            """;
        var adapter = CreateAdapter(_ => JsonResponse(payload));
        var source = CreateSource(new
        {
            url = "https://api.example.com/opps",
            agencyName = "Test",
            resultsJsonPath = "$.data.items",
            fieldMap = new { externalId = "opportunity_id", title = "heading" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].ExternalId.Should().Be("N-001");
    }

    [Fact]
    public async Task Fails_when_required_parameters_missing()
    {
        var adapter = CreateAdapter(_ => JsonResponse("[]"));
        var source = SourceDefinition.Create(
            TenantId, SourceAdapterType.GenericRest, "Test", "{}");

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("fieldMap");
    }

    [Fact]
    public async Task Fails_when_results_path_does_not_resolve_to_array()
    {
        var payload = """{ "items": { "nope": "object not array" } }""";
        var adapter = CreateAdapter(_ => JsonResponse(payload));
        var source = CreateSource(new
        {
            url = "https://api.example.com/opps",
            agencyName = "Test",
            resultsJsonPath = "$.items",
            fieldMap = new { externalId = "id", title = "title" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("JSON array");
    }

    [Fact]
    public async Task Skips_items_without_external_id_or_title()
    {
        var payload = """
            [
              { "id": "", "name": "Missing id" },
              { "id": "K-1", "name": "" },
              { "id": "K-2", "name": "Good one" }
            ]
            """;
        var adapter = CreateAdapter(_ => JsonResponse(payload));
        var source = CreateSource(new
        {
            url = "https://api.example.com/opps",
            agencyName = "Test",
            resultsJsonPath = "$",
            fieldMap = new { externalId = "id", title = "name" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value![0].ExternalId.Should().Be("K-2");
    }

    [Fact]
    public async Task Fails_on_http_error()
    {
        var adapter = CreateAdapter(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var source = CreateSource(new
        {
            url = "https://api.example.com/err",
            agencyName = "Test",
            fieldMap = new { externalId = "id", title = "title" }
        });

        var result = await adapter.FetchAsync(source, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("REST fetch failed");
    }

    [Fact]
    public async Task Sends_custom_headers()
    {
        HttpRequestMessage? captured = null;
        var adapter = CreateAdapter(req =>
        {
            captured = req;
            return JsonResponse("[]");
        });
        var source = CreateSource(new
        {
            url = "https://api.example.com/opps",
            agencyName = "Test",
            headers = new Dictionary<string, string> { ["Authorization"] = "Bearer abc123" },
            resultsJsonPath = "$",
            fieldMap = new { externalId = "id", title = "title" }
        });

        await adapter.FetchAsync(source, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Headers.GetValues("Authorization").Should().Contain("Bearer abc123");
    }

    [Fact]
    public async Task Supports_post_with_request_body()
    {
        HttpRequestMessage? captured = null;
        var adapter = CreateAdapter(req =>
        {
            captured = req;
            return JsonResponse("[]");
        });
        var source = CreateSource(new
        {
            url = "https://api.example.com/search",
            agencyName = "Test",
            method = "POST",
            requestBody = """{"q":"contact center"}""",
            resultsJsonPath = "$",
            fieldMap = new { externalId = "id", title = "title" }
        });

        await adapter.FetchAsync(source, CancellationToken.None);

        captured!.Method.Should().Be(HttpMethod.Post);
        var body = await captured.Content!.ReadAsStringAsync();
        body.Should().Contain("contact center");
    }
}
