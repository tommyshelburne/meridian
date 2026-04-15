using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class OutreachSequenceConfiguration : IEntityTypeConfiguration<OutreachSequence>
{
    public void Configure(EntityTypeBuilder<OutreachSequence> builder)
    {
        builder.ToTable("outreach_sequences");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.OpportunityType).HasColumnName("opportunity_type").HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.AgencyType).HasColumnName("agency_type").HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.LastUsedAt).HasColumnName("last_used_at");

        builder.HasMany(s => s.Steps)
            .WithOne()
            .HasForeignKey(st => st.SequenceId);
    }
}
