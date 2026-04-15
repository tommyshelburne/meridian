using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class SequenceSnapshotConfiguration : IEntityTypeConfiguration<SequenceSnapshot>
{
    public void Configure(EntityTypeBuilder<SequenceSnapshot> builder)
    {
        builder.ToTable("sequence_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.SequenceId).HasColumnName("sequence_id");
        builder.Property(s => s.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
    }
}
