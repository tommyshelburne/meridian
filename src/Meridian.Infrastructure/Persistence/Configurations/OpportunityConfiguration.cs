using Meridian.Domain.Opportunities;
using Meridian.Domain.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("opportunities");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(o => o.ExternalId).HasColumnName("external_id").HasMaxLength(500).IsRequired();
        builder.Property(o => o.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(50);
        builder.Property(o => o.Title).HasColumnName("title").HasMaxLength(1000).IsRequired();
        builder.Property(o => o.Description).HasColumnName("description");
        builder.Property(o => o.EstimatedValue).HasColumnName("estimated_value").HasColumnType("decimal(18,2)");
        builder.Property(o => o.PostedDate).HasColumnName("posted_date");
        builder.Property(o => o.ResponseDeadline).HasColumnName("response_deadline");
        builder.Property(o => o.NaicsCode).HasColumnName("naics_code").HasMaxLength(20);
        builder.Property(o => o.ProcurementVehicle).HasColumnName("procurement_vehicle").HasConversion<string>().HasMaxLength(50);
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.WatchedSince).HasColumnName("watched_since");
        builder.Property(o => o.LastAmendedAt).HasColumnName("last_amended_at");

        builder.OwnsOne(o => o.Agency, a =>
        {
            a.Property(x => x.Name).HasColumnName("agency_name").HasMaxLength(500).IsRequired();
            a.Property(x => x.Type).HasColumnName("agency_type").HasConversion<string>().HasMaxLength(30);
            a.Property(x => x.State).HasColumnName("agency_state").HasMaxLength(5);
        });

        builder.OwnsOne(o => o.Score, s =>
        {
            s.Property(x => x.Total).HasColumnName("score_total");
            s.Property(x => x.Verdict).HasColumnName("score_verdict").HasConversion<string>().HasMaxLength(20);
            s.Property(x => x.ScoredAt).HasColumnName("scored_at");
        });

        builder.HasMany(o => o.Contacts)
            .WithOne()
            .HasForeignKey(c => c.OpportunityId);

        builder.HasIndex(o => new { o.TenantId, o.ExternalId }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.WatchedSince }).HasFilter("watched_since IS NOT NULL");
    }
}
