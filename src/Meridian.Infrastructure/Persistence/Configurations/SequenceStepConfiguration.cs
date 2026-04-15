using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class SequenceStepConfiguration : IEntityTypeConfiguration<SequenceStep>
{
    public void Configure(EntityTypeBuilder<SequenceStep> builder)
    {
        builder.ToTable("sequence_steps");
        builder.HasKey(s => new { s.SequenceId, s.StepNumber });
        builder.Property(s => s.SequenceId).HasColumnName("sequence_id");
        builder.Property(s => s.StepNumber).HasColumnName("step_number");
        builder.Property(s => s.DelayDays).HasColumnName("delay_days");
        builder.Property(s => s.TemplateId).HasColumnName("template_id");
        builder.Property(s => s.Subject).HasColumnName("subject").HasMaxLength(500).IsRequired();
        builder.Property(s => s.SendWindowStart).HasColumnName("send_window_start");
        builder.Property(s => s.SendWindowEnd).HasColumnName("send_window_end");
        builder.Property(s => s.SendWindowJitterMinutes).HasColumnName("jitter_minutes");
    }
}
