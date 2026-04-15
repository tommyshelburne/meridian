using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class SuppressionEntryConfiguration : IEntityTypeConfiguration<SuppressionEntry>
{
    public void Configure(EntityTypeBuilder<SuppressionEntry> builder)
    {
        builder.ToTable("suppression_entries");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.Value).HasColumnName("value").HasMaxLength(320).IsRequired();
        builder.Property(s => s.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Reason).HasColumnName("reason").HasMaxLength(200).IsRequired();
        builder.Property(s => s.AddedAt).HasColumnName("added_at");

        builder.HasIndex(s => new { s.TenantId, s.Value }).IsUnique();
    }
}
