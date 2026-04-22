using Meridian.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class SourceDefinitionConfiguration : IEntityTypeConfiguration<SourceDefinition>
{
    public void Configure(EntityTypeBuilder<SourceDefinition> builder)
    {
        builder.ToTable("source_definitions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.AdapterType).HasColumnName("adapter_type")
            .HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.ParametersJson).HasColumnName("parameters").IsRequired();
        builder.Property(s => s.Schedule).HasColumnName("schedule").HasMaxLength(100);
        builder.Property(s => s.IsEnabled).HasColumnName("is_enabled");
        builder.Property(s => s.LastRunAt).HasColumnName("last_run_at");
        builder.Property(s => s.LastRunStatus).HasColumnName("last_run_status")
            .HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.LastRunError).HasColumnName("last_run_error").HasMaxLength(2000);
        builder.Property(s => s.ConsecutiveFailures).HasColumnName("consecutive_failures");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.IsEnabled });
    }
}
