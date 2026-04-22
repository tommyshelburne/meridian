using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion.UsaSpending;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class UsaSpendingClientTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static UsaSpendingClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHandler(handler));
        var options = Options.Create(new UsaSpendingOptions
        {
            BaseUrl = "https://api.usaspending.gov/api/v2/search/spending_by_award/",
            PageSize = 50,
            MaxPages = 2,
            DefaultLookbackDays = 30
        });
        return new UsaSpendingClient(httpClient, options, NullLogger<UsaSpendingClient>.Instance);
    }

    private static SourceDefinition CreateSource(params string[] naicsCodes)
    {
        var paramsJson = naicsCodes.Length == 0
            ? "{}"
            : JsonSerializer.Serialize(new { naicsCodes, lookbackDays = 30 });
        return SourceDefinition.Create(TenantId, SourceAdapterType.UsaSpending, "USASpending", paramsJson);
    }

    private static HttpResponseMessage JsonResponse(object body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task Fetches_awards_and_maps_to_ingested_opportunities()
    {
        var client = CreateClient(_ => JsonResponse(new UsaSpendingSearchResponse
        {
            Results = new List<UsaSpendingResult>
            {
                new()
                {
                    AwardId = "USA-AWARD-001",
                    Description = "Call center operations support",
                    AwardAmount = 2_500_000m,
                    AwardingAgency = "Department of Health and Human Services",
                    AwardingSubAgency = "Centers for Medicare & Medicaid Services",
                    NaicsCode = "561422",
                    StartDate = "2025-01-01",
                    EndDate = "2026-12-31",
                    LastModifiedDate = "2026-04-15",
                    RecipientName = "Acme Corp"
                }
            },
            PageMetadata = new UsaSpendingPageMetadata { Page = 1, HasNext = false }
        }));

        var result = await client.FetchAsync(CreateSource("561422"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var opp = result.Value![0];
        opp.ExternalId.Should().Be("USA-AWARD-001");
        opp.Title.Should().Be("Call center operations support");
        opp.AgencyName.Should().Be("Centers for Medicare & Medicaid Services");
        opp.EstimatedValue.Should().Be(2_500_000m);
        opp.NaicsCode.Should().Be("561422");
        opp.Metadata.Should().ContainKey("recipient").WhoseValue.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task Classifies_defense_agency()
    {
        var client = CreateClient(_ => JsonResponse(new UsaSpendingSearchResponse
        {
            Results = new List<UsaSpendingResult>
            {
                new()
                {
                    AwardId = "USA-DOD-001",
                    Description = "Army helpdesk support",
                    AwardingAgency = "Department of Defense",
                    AwardingSubAgency = "Department of the Army"
                }
            },
            PageMetadata = new UsaSpendingPageMetadata { HasNext = false }
        }));

        var result = await client.FetchAsync(CreateSource("561422"), CancellationToken.None);

        result.Value![0].AgencyType.Should().Be(AgencyType.FederalDefense);
    }

    [Fact]
    public async Task Skips_entries_without_award_id()
    {
        var client = CreateClient(_ => JsonResponse(new UsaSpendingSearchResponse
        {
            Results = new List<UsaSpendingResult>
            {
                new() { AwardId = null, Description = "No ID" },
                new() { AwardId = "USA-VALID", Description = "Good one" }
            },
            PageMetadata = new UsaSpendingPageMetadata { HasNext = false }
        }));

        var result = await client.FetchAsync(CreateSource("561422"), CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value![0].ExternalId.Should().Be("USA-VALID");
    }

    [Fact]
    public async Task Paginates_when_has_next_true()
    {
        var page = 0;
        var client = CreateClient(_ =>
        {
            page++;
            return page switch
            {
                1 => JsonResponse(new UsaSpendingSearchResponse
                {
                    Results = new List<UsaSpendingResult>
                    {
                        new() { AwardId = "USA-001", Description = "First" }
                    },
                    PageMetadata = new UsaSpendingPageMetadata { Page = 1, HasNext = true }
                }),
                _ => JsonResponse(new UsaSpendingSearchResponse
                {
                    Results = new List<UsaSpendingResult>
                    {
                        new() { AwardId = "USA-002", Description = "Second" }
                    },
                    PageMetadata = new UsaSpendingPageMetadata { Page = 2, HasNext = false }
                })
            };
        });

        var result = await client.FetchAsync(CreateSource("561422"), CancellationToken.None);

        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handles_empty_response()
    {
        var client = CreateClient(_ => JsonResponse(new UsaSpendingSearchResponse()));

        var result = await client.FetchAsync(CreateSource("561422"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handles_http_error_gracefully()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await client.FetchAsync(CreateSource("561422"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Dedupes_awards_with_same_id()
    {
        var page = 0;
        var client = CreateClient(_ =>
        {
            page++;
            return page == 1
                ? JsonResponse(new UsaSpendingSearchResponse
                {
                    Results = new List<UsaSpendingResult>
                    {
                        new() { AwardId = "USA-DUP", Description = "First" }
                    },
                    PageMetadata = new UsaSpendingPageMetadata { HasNext = true }
                })
                : JsonResponse(new UsaSpendingSearchResponse
                {
                    Results = new List<UsaSpendingResult>
                    {
                        new() { AwardId = "USA-DUP", Description = "Second copy" }
                    },
                    PageMetadata = new UsaSpendingPageMetadata { HasNext = false }
                });
        });

        var result = await client.FetchAsync(CreateSource("561422"), CancellationToken.None);

        result.Value.Should().HaveCount(1);
    }
}
