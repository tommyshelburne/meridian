using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class OutboundConfigurationConfiguration : IEntityTypeConfiguration<OutboundConfiguration>
{
    public void Configure(EntityTypeBuilder<OutboundConfiguration> builder)
    {
        builder.ToTable("outbound_configurations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.ProviderType).HasColumnName("provider_type")
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.EncryptedApiKey).HasColumnName("encrypted_api_key").HasMaxLength(2000);
        builder.Property(c => c.FromAddress).HasColumnName("from_address").HasMaxLength(320).IsRequired();
        builder.Property(c => c.FromName).HasColumnName("from_name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.ReplyToAddress).HasColumnName("reply_to_address").HasMaxLength(320);
        builder.Property(c => c.PhysicalAddress).HasColumnName("physical_address").HasMaxLength(500).IsRequired();
        builder.Property(c => c.UnsubscribeBaseUrl).HasColumnName("unsubscribe_base_url").HasMaxLength(1000).IsRequired();
        builder.Property(c => c.IsEnabled).HasColumnName("is_enabled");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => c.TenantId).IsUnique();
    }
}
