using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class EmailActivityConfiguration : IEntityTypeConfiguration<EmailActivity>
{
    public void Configure(EntityTypeBuilder<EmailActivity> builder)
    {
        builder.ToTable("email_activities");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.EnrollmentId).HasColumnName("enrollment_id");
        builder.Property(e => e.ContactId).HasColumnName("contact_id");
        builder.Property(e => e.OpportunityId).HasColumnName("opportunity_id");
        builder.Property(e => e.StepNumber).HasColumnName("step_number");
        builder.Property(e => e.Subject).HasColumnName("subject").HasMaxLength(500).IsRequired();
        builder.Property(e => e.BodyText).HasColumnName("body_text").IsRequired();
        builder.Property(e => e.SentAt).HasColumnName("sent_at");
        builder.Property(e => e.MessageId).HasColumnName("message_id").HasMaxLength(500);
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.RepliedAt).HasColumnName("replied_at");
        builder.Property(e => e.BouncedAt).HasColumnName("bounced_at");
        builder.Property(e => e.BouncedReason).HasColumnName("bounced_reason").HasMaxLength(500);

        builder.HasIndex(e => new { e.TenantId, e.MessageId }).HasFilter("message_id IS NOT NULL");
        builder.HasIndex(e => new { e.TenantId, e.ContactId, e.SentAt });
    }
}
