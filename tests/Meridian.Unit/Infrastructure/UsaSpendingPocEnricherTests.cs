using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Ingestion.UsaSpending;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Unit.Infrastructure;

public class UsaSpendingPocEnricherTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string SearchUrl = "https://api.usaspending.gov/api/v2/search/spending_by_award/";
    private const string AwardsUrl = "https://api.usaspending.gov/api/v2/awards/";

    private static UsaSpendingPocEnricher CreateEnricher(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHandler(handler));
        var options = Options.Create(new UsaSpendingOptions
        {
            BaseUrl = SearchUrl,
            AwardDetailBaseUrl = AwardsUrl,
            PocEnricherMaxAwards = 3,
            PocEnricherLookbackDays = 365,
            PocEnricherBaseConfidence = 0.65f
        });
        return new UsaSpendingPocEnricher(httpClient, options, NullLogger<UsaSpendingPocEnricher>.Instance);
    }

    private static Opportunity CreateOpportunity(string? naics = "541512", string agency = "GSA")
        => Opportunity.Create(TenantId, $"OPP-{Guid.NewGuid()}", OpportunitySource.SamGov,
            "RFP", "Description",
            Agency.Create(agency, AgencyType.FederalCivilian),
            DateTimeOffset.UtcNow,
            naicsCode: naics);

    private static HttpResponseMessage JsonResponse(object body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task Returns_empty_when_opportunity_has_no_naics()
    {
        var enricher = CreateEnricher(_ => throw new InvalidOperationException("Should not call HTTP"));

        var result = await enricher.EnrichAsync(CreateOpportunity(naics: null), TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Maps_business_representative_to_contact_with_base_confidence()
    {
        var enricher = CreateEnricher(req =>
        {
            if (req.RequestUri!.AbsoluteUri.StartsWith(SearchUrl))
            {
                return JsonResponse(new UsaSpendingSearchResponse
                {
                    Results = new List<UsaSpendingResult>
                    {
                        new() { AwardId = "AWARD-1", Description = "Past award" }
                    }
                });
            }

            return JsonResponse(new UsaSpendingAwardDetail
            {
                Recipient = new UsaSpendingAwardRecipient
                {
                    RecipientName = "Acme Corp",
                    PrimaryBusinessRepresentative = new UsaSpendingBusinessRepresentative
                    {
                        Name = "Jane Smith",
                        Title = "VP Federal",
                        Email = "jane@acme.com",
                        Phone = "555-1212"
                    }
                }
            });
        });

        var result = await enricher.EnrichAsync(CreateOpportunity(), TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var contact = result.Value.Should().ContainSingle().Subject;
        contact.FullName.Should().Be("Jane Smith");
        contact.Title.Should().Be("VP Federal");
        contact.Email.Should().Be("jane@acme.com");
        contact.Phone.Should().Be("555-1212");
        contact.ConfidenceScore.Should().Be(0.65f);
        contact.Source.Should().Be(ContactSource.UsaSpending);
    }

    [Fact]
    public async Task Skips_award_with_no_recipient_email()
    {
        var awardCalls = 0;
        var enricher = CreateEnricher(req =>
        {
            if (req.RequestUri!.AbsoluteUri.StartsWith(SearchUrl))
            {
                return JsonResponse(new UsaSpendingSearchResponse
                {
                    Results = new List<UsaSpendingResult>
                    {
                        new() { AwardId = "AWARD-NO-EMAIL" },
                        new() { AwardId = "AWARD-OK" }
                    }
                });
            }

            awardCalls++;
            if (req.RequestUri.AbsoluteUri.Contains("AWARD-NO-EMAIL"))
                return JsonResponse(new UsaSpendingAwardDetail
                {
                    Recipient = new UsaSpendingAwardRecipient
                    {
                        PrimaryBusinessRepresentative = new UsaSpendingBusinessRepresentative
                        {
                            Name = "No Email", Email = null
                        }
                    }
                });

            return JsonResponse(new UsaSpendingAwardDetail
            {
                Recipient = new UsaSpendingAwardRecipient
                {
                    PrimaryBusinessRepresentative = new UsaSpendingBusinessRepresentative
                    {
                        Name = "Has Email", Email = "ok@vendor.com"
                    }
                }
            });
        });

        var result = await enricher.EnrichAsync(CreateOpportunity(), TenantId, CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value![0].Email.Should().Be("ok@vendor.com");
        awardCalls.Should().Be(2);
    }

    [Fact]
    public async Task Dedupes_contacts_by_email()
    {
        var enricher = CreateEnricher(req =>
        {
            if (req.RequestUri!.AbsoluteUri.StartsWith(SearchUrl))
            {
                return JsonResponse(new UsaSpendingSearchResponse
                {
                    Results = new List<UsaSpendingResult>
                    {
                        new() { AwardId = "AWARD-1" },
                        new() { AwardId = "AWARD-2" }
                    }
                });
            }

            return JsonResponse(new UsaSpendingAwardDetail
            {
                Recipient = new UsaSpendingAwardRecipient
                {
                    PrimaryBusinessRepresentative = new UsaSpendingBusinessRepresentative
                    {
                        Name = "Same Person", Email = "same@vendor.com"
                    }
                }
            });
        });

        var result = await enricher.EnrichAsync(CreateOpportunity(), TenantId, CancellationToken.None);

        result.Value.Should().ContainSingle();
    }

    [Fact]
    public async Task Returns_empty_when_no_awards_found()
    {
        var enricher = CreateEnricher(_ => JsonResponse(new UsaSpendingSearchResponse()));

        var result = await enricher.EnrichAsync(CreateOpportunity(), TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_failure_returns_empty_without_throwing()
    {
        var enricher = CreateEnricher(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await enricher.EnrichAsync(CreateOpportunity(), TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Award_detail_failure_skips_just_that_award()
    {
        var enricher = CreateEnricher(req =>
        {
            if (req.RequestUri!.AbsoluteUri.StartsWith(SearchUrl))
            {
                return JsonResponse(new UsaSpendingSearchResponse
                {
                    Results = new List<UsaSpendingResult>
                    {
                        new() { AwardId = "BROKEN" },
                        new() { AwardId = "GOOD" }
                    }
                });
            }

            if (req.RequestUri.AbsoluteUri.Contains("BROKEN"))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            return JsonResponse(new UsaSpendingAwardDetail
            {
                Recipient = new UsaSpendingAwardRecipient
                {
                    PrimaryBusinessRepresentative = new UsaSpendingBusinessRepresentative
                    {
                        Name = "Fine", Email = "fine@vendor.com"
                    }
                }
            });
        });

        var result = await enricher.EnrichAsync(CreateOpportunity(), TenantId, CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value![0].Email.Should().Be("fine@vendor.com");
    }
}
