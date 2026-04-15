using Meridian.Domain.Outreach;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class OutreachTemplateConfiguration : IEntityTypeConfiguration<OutreachTemplate>
{
    public void Configure(EntityTypeBuilder<OutreachTemplate> builder)
    {
        builder.ToTable("outreach_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(t => t.SubjectTemplate).HasColumnName("subject_template").IsRequired();
        builder.Property(t => t.BodyTemplate).HasColumnName("body_template").IsRequired();
        builder.Property(t => t.Version).HasColumnName("version");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.ModifiedAt).HasColumnName("modified_at");
    }
}
