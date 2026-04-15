using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;

namespace Meridian.Unit.Domain;

public class OpportunityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Agency TestAgency = Agency.Create("VA", AgencyType.FederalCivilian, 1);

    [Fact]
    public void Create_sets_status_to_New()
    {
        var opp = Opportunity.Create(TenantId, "SAM-123", OpportunitySource.SamGov,
            "Contact Center RFI", "Description", TestAgency, DateTimeOffset.UtcNow);

        opp.Status.Should().Be(OpportunityStatus.New);
        opp.Id.Should().NotBeEmpty();
        opp.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Create_requires_title()
    {
        var act = () => Opportunity.Create(TenantId, "SAM-123", OpportunitySource.SamGov,
            "", "Description", TestAgency, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApplyScore_sets_status_based_on_verdict()
    {
        var opp = Opportunity.Create(TenantId, "SAM-123", OpportunitySource.SamGov,
            "Contact Center", "Desc", TestAgency, DateTimeOffset.UtcNow);

        var noBidScore = BidScore.Create(0, 1, 0, 0, 0, 0, 0, 0);
        opp.ApplyScore(noBidScore);

        opp.Status.Should().Be(OpportunityStatus.NoBid);
    }

    [Fact]
    public void Watch_sets_status_and_timestamp()
    {
        var opp = Opportunity.Create(TenantId, "SAM-123", OpportunitySource.SamGov,
            "Contact Center", "Desc", TestAgency, DateTimeOffset.UtcNow);

        opp.Watch();

        opp.Status.Should().Be(OpportunityStatus.Watching);
        opp.WatchedSince.Should().NotBeNull();
    }

    [Fact]
    public void AddContact_prevents_duplicates()
    {
        var opp = Opportunity.Create(TenantId, "SAM-123", OpportunitySource.SamGov,
            "Contact Center", "Desc", TestAgency, DateTimeOffset.UtcNow);

        var contactId = Guid.NewGuid();
        opp.AddContact(OpportunityContact.Create(opp.Id, contactId));
        opp.AddContact(OpportunityContact.Create(opp.Id, contactId));

        opp.Contacts.Should().HaveCount(1);
    }

    [Fact]
    public void SetSeatEstimate_stores_seats_and_confidence()
    {
        var opp = Opportunity.Create(TenantId, "SAM-123", OpportunitySource.SamGov,
            "Contact Center", "Desc", TestAgency, DateTimeOffset.UtcNow);

        opp.SetSeatEstimate(200, SeatEstimateConfidence.High);

        opp.EstimatedSeats.Should().Be(200);
        opp.SeatConfidence.Should().Be(SeatEstimateConfidence.High);
    }
}
