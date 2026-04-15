using Meridian.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class RagMemoryConfiguration : IEntityTypeConfiguration<RagMemory>
{
    public void Configure(EntityTypeBuilder<RagMemory> builder)
    {
        builder.ToTable("rag_memories");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(r => r.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
        builder.Property(r => r.EntityId).HasColumnName("entity_id");
        builder.Property(r => r.Content).HasColumnName("content").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        // pgvector embedding column added via raw SQL migration (EF Core doesn't natively map vector types)

        builder.HasIndex(r => new { r.TenantId, r.EntityType, r.EntityId });
    }
}
