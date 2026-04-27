using Meridian.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class CrmConnectionConfiguration : IEntityTypeConfiguration<CrmConnection>
{
    public void Configure(EntityTypeBuilder<CrmConnection> builder)
    {
        builder.ToTable("crm_connections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.Provider).HasColumnName("provider")
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.EncryptedAuthToken).HasColumnName("encrypted_auth_token")
            .HasMaxLength(4000).IsRequired();
        builder.Property(c => c.EncryptedRefreshToken).HasColumnName("encrypted_refresh_token")
            .HasMaxLength(4000);
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.ApiBaseUrl).HasColumnName("api_base_url").HasMaxLength(500);
        builder.Property(c => c.DefaultPipelineId).HasColumnName("default_pipeline_id").HasMaxLength(200);
        builder.Property(c => c.IsActive).HasColumnName("is_active");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => c.TenantId).IsUnique();
    }
}
