using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class OutreachEnrollmentConfiguration : IEntityTypeConfiguration<OutreachEnrollment>
{
    public void Configure(EntityTypeBuilder<OutreachEnrollment> builder)
    {
        builder.ToTable("outreach_enrollments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.OpportunityId).HasColumnName("opportunity_id");
        builder.Property(e => e.ContactId).HasColumnName("contact_id");
        builder.Property(e => e.SequenceId).HasColumnName("sequence_id");
        builder.Property(e => e.SequenceSnapshotId).HasColumnName("sequence_snapshot_id");
        builder.Property(e => e.CurrentStep).HasColumnName("current_step");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.EnrolledAt).HasColumnName("enrolled_at");
        builder.Property(e => e.NextSendAt).HasColumnName("next_send_at");
        builder.Property(e => e.PausedReason).HasColumnName("paused_reason").HasMaxLength(500);

        // Duplicate guard: one enrollment per contact per opportunity
        builder.HasIndex(e => new { e.TenantId, e.ContactId, e.OpportunityId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Status, e.NextSendAt });
    }
}
