using Meridian.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class OidcConfigConfiguration : IEntityTypeConfiguration<OidcConfig>
{
    public void Configure(EntityTypeBuilder<OidcConfig> builder)
    {
        builder.ToTable("oidc_configs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(c => c.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(40);
        builder.Property(c => c.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.Authority).HasColumnName("authority").HasMaxLength(500).IsRequired();
        builder.Property(c => c.ClientId).HasColumnName("client_id").HasMaxLength(300).IsRequired();
        builder.Property(c => c.ClientSecret).HasColumnName("client_secret").HasMaxLength(2000).IsRequired();
        builder.Property(c => c.Scopes).HasColumnName("scopes").HasMaxLength(500);
        builder.Property(c => c.EmailClaim).HasColumnName("email_claim").HasMaxLength(100);
        builder.Property(c => c.NameClaim).HasColumnName("name_claim").HasMaxLength(100);
        builder.Property(c => c.IsEnabled).HasColumnName("is_enabled");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => new { c.TenantId, c.ProviderKey }).IsUnique();
    }
}
