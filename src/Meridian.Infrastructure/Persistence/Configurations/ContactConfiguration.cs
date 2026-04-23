using Meridian.Domain.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.FullName).HasColumnName("full_name").HasMaxLength(300).IsRequired();
        builder.Property(c => c.Title).HasColumnName("title").HasMaxLength(200);
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(320);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(c => c.LinkedInUrl).HasColumnName("linkedin_url").HasMaxLength(500);
        builder.Property(c => c.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.ConfidenceScore).HasColumnName("confidence_score");
        builder.Property(c => c.IsOptedOut).HasColumnName("is_opted_out");
        builder.Property(c => c.IsBounced).HasColumnName("is_bounced");
        builder.Property(c => c.SoftBounceCount).HasColumnName("soft_bounce_count").HasDefaultValue(0);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.LastVerifiedAt).HasColumnName("last_verified_at");

        builder.OwnsOne(c => c.Agency, a =>
        {
            a.Property(x => x.Name).HasColumnName("agency_name").HasMaxLength(500).IsRequired();
            a.Property(x => x.Type).HasColumnName("agency_type").HasConversion<string>().HasMaxLength(30);
            a.Property(x => x.State).HasColumnName("agency_state").HasMaxLength(5);
        });

        builder.HasIndex(c => new { c.TenantId, c.Email }).HasFilter("email IS NOT NULL");
        builder.HasIndex(c => new { c.TenantId, c.IsOptedOut });
    }
}
