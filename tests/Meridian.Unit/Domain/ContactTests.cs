using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;

namespace Meridian.Unit.Domain;

public class ContactTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Agency TestAgency = Agency.Create("VA", AgencyType.FederalCivilian, 1);

    [Fact]
    public void Create_sets_all_fields()
    {
        var contact = Contact.Create(TenantId, "John Smith", TestAgency, ContactSource.SamGov, 0.9f,
            email: "john@va.gov", title: "Program Manager");

        contact.FullName.Should().Be("John Smith");
        contact.Email.Should().Be("john@va.gov");
        contact.ConfidenceScore.Should().Be(0.9f);
        contact.IsOptedOut.Should().BeFalse();
        contact.IsBounced.Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_invalid_confidence()
    {
        var act = () => Contact.Create(TenantId, "John", TestAgency, ContactSource.SamGov, 1.5f);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IsEnrollable_true_when_valid()
    {
        var contact = Contact.Create(TenantId, "John", TestAgency, ContactSource.SamGov, 0.9f,
            email: "john@va.gov");

        contact.IsEnrollable.Should().BeTrue();
    }

    [Fact]
    public void IsEnrollable_false_when_opted_out()
    {
        var contact = Contact.Create(TenantId, "John", TestAgency, ContactSource.SamGov, 0.9f,
            email: "john@va.gov");
        contact.OptOut();

        contact.IsEnrollable.Should().BeFalse();
    }

    [Fact]
    public void IsEnrollable_false_when_bounced()
    {
        var contact = Contact.Create(TenantId, "John", TestAgency, ContactSource.SamGov, 0.9f,
            email: "john@va.gov");
        contact.MarkBounced();

        contact.IsEnrollable.Should().BeFalse();
    }

    [Fact]
    public void IsEnrollable_false_when_low_confidence()
    {
        var contact = Contact.Create(TenantId, "John", TestAgency, ContactSource.SamGov, 0.3f,
            email: "john@va.gov");

        contact.IsEnrollable.Should().BeFalse();
    }

    [Fact]
    public void IsEnrollable_false_when_no_email()
    {
        var contact = Contact.Create(TenantId, "John", TestAgency, ContactSource.SamGov, 0.9f);

        contact.IsEnrollable.Should().BeFalse();
    }
}
