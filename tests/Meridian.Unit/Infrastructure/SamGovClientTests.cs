using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Infrastructure.Ingestion.SamGov;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class SamGovClientTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static SamGovClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHandler(handler));
        var options = Options.Create(new SamGovOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://api.sam.gov/opportunities/v2/search",
            Keywords = new[] { "contact center" },
            PageSize = 25,
            MaxPages = 2,
            LookbackDays = 7
        });
        return new SamGovClient(httpClient, options, NullLogger<SamGovClient>.Instance);
    }

    private static SamGovSearchResponse CreateSearchResponse(params SamGovOpportunity[] opps)
    {
        return new SamGovSearchResponse
        {
            TotalRecords = opps.Length,
            OpportunitiesData = opps.ToList()
        };
    }

    private static HttpResponseMessage JsonResponse(object body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task Fetches_opportunities_from_search_results()
    {
        var client = CreateClient(_ => JsonResponse(CreateSearchResponse(
            new SamGovOpportunity
            {
                NoticeId = "SAM-001",
                Title = "Contact Center Support Services",
                Description = "Provide contact center staffing",
                Department = "Department of Veterans Affairs",
                SubTier = "VA",
                PostedDate = "01/10/2026",
                ResponseDeadline = "02/10/2026",
                NaicsCode = "561422"
            })));

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].Title.Should().Be("Contact Center Support Services");
        result.Value[0].Source.Should().Be(OpportunitySource.SamGov);
        result.Value[0].ExternalId.Should().Be("SAM-001");
    }

    [Fact]
    public async Task Deduplicates_across_keywords()
    {
        var options = Options.Create(new SamGovOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://api.sam.gov/opportunities/v2/search",
            Keywords = new[] { "contact center", "call center" },
            PageSize = 25,
            MaxPages = 2,
            LookbackDays = 7
        });
        var client = new SamGovClient(
            new HttpClient(new FakeHandler(_ => JsonResponse(CreateSearchResponse(
                new SamGovOpportunity { NoticeId = "SAM-001", Title = "Contact Center Services" })))),
            options,
            NullLogger<SamGovClient>.Instance);

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1, "same NoticeId from two keywords should deduplicate");
    }

    [Fact]
    public async Task Detects_recompete_from_description()
    {
        var client = CreateClient(_ => JsonResponse(CreateSearchResponse(
            new SamGovOpportunity
            {
                NoticeId = "SAM-002",
                Title = "IVR Modernization",
                Description = "Recompete of existing Nuance contract for IVR services"
            })));

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.Value![0].IsRecompete.Should().BeTrue();
    }

    [Fact]
    public async Task Estimates_seats_from_award_amount()
    {
        var client = CreateClient(_ => JsonResponse(CreateSearchResponse(
            new SamGovOpportunity
            {
                NoticeId = "SAM-003",
                Title = "Call Center Operations",
                Award = new SamGovAward { Amount = 5_000_000m }
            })));

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        // 5,000,000 / 199 / 12 ≈ 2,093 seats
        result.Value![0].EstimatedSeats.Should().BeGreaterThan(2000);
    }

    [Fact]
    public async Task Classifies_defense_agency()
    {
        var client = CreateClient(_ => JsonResponse(CreateSearchResponse(
            new SamGovOpportunity
            {
                NoticeId = "SAM-004",
                Title = "Contact Center Helpdesk",
                Department = "Department of Defense",
                SubTier = "Army"
            })));

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.Value![0].Agency.Type.Should().Be(AgencyType.FederalDefense);
    }

    [Fact]
    public async Task Classifies_tier_1_agency()
    {
        var client = CreateClient(_ => JsonResponse(CreateSearchResponse(
            new SamGovOpportunity
            {
                NoticeId = "SAM-005",
                Title = "Customer Service Center",
                Department = "Department of Veterans Affairs",
                SubTier = "Veterans Affairs"
            })));

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.Value![0].Agency.Tier.Should().Be(1);
    }

    [Fact]
    public async Task Handles_empty_response()
    {
        var client = CreateClient(_ => JsonResponse(new SamGovSearchResponse()));

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handles_http_error_gracefully()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_entries_without_notice_id()
    {
        var client = CreateClient(_ => JsonResponse(CreateSearchResponse(
            new SamGovOpportunity { NoticeId = "", Title = "No ID" },
            new SamGovOpportunity { NoticeId = "SAM-VALID", Title = "Contact Center Services" })));

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value![0].ExternalId.Should().Be("SAM-VALID");
    }

    [Fact]
    public async Task Paginates_when_more_results_exist()
    {
        var page = 0;
        var client = CreateClient(_ =>
        {
            page++;
            return page switch
            {
                1 => JsonResponse(new SamGovSearchResponse
                {
                    TotalRecords = 30,
                    OpportunitiesData = Enumerable.Range(1, 25)
                        .Select(i => new SamGovOpportunity { NoticeId = $"SAM-{i:000}", Title = "Contact Center" })
                        .ToList()
                }),
                2 => JsonResponse(new SamGovSearchResponse
                {
                    TotalRecords = 30,
                    OpportunitiesData = Enumerable.Range(26, 5)
                        .Select(i => new SamGovOpportunity { NoticeId = $"SAM-{i:000}", Title = "Contact Center" })
                        .ToList()
                }),
                _ => JsonResponse(new SamGovSearchResponse())
            };
        });

        var result = await client.FetchOpportunitiesAsync(TenantId, CancellationToken.None);

        result.Value.Should().HaveCount(30);
    }
}
