using Meridian.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class OpportunityContactConfiguration : IEntityTypeConfiguration<OpportunityContact>
{
    public void Configure(EntityTypeBuilder<OpportunityContact> builder)
    {
        builder.ToTable("opportunity_contacts");
        builder.HasKey(oc => new { oc.OpportunityId, oc.ContactId });
        builder.Property(oc => oc.OpportunityId).HasColumnName("opportunity_id");
        builder.Property(oc => oc.ContactId).HasColumnName("contact_id");
        builder.Property(oc => oc.IsPrimary).HasColumnName("is_primary");
    }
}
