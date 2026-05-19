using Meridian.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class WebhookPayloadRecordConfiguration : IEntityTypeConfiguration<WebhookPayloadRecord>
{
    public void Configure(EntityTypeBuilder<WebhookPayloadRecord> builder)
    {
        builder.ToTable("webhook_payloads");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(p => p.SourceDefinitionId).HasColumnName("source_definition_id").IsRequired();
        builder.Property(p => p.RawJson).HasColumnName("raw_json").IsRequired();
        builder.Property(p => p.ReceivedAt).HasColumnName("received_at").IsRequired();

        // Drained by source id; intentionally no tenant query filter — the
        // ingestion run resolves the tenant from the drained payload itself.
        builder.HasIndex(p => p.SourceDefinitionId);
    }
}
