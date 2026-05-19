using Meridian.Domain.Markets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

public class ProcurementMarketCellConfiguration : IEntityTypeConfiguration<ProcurementMarketCell>
{
    public void Configure(EntityTypeBuilder<ProcurementMarketCell> builder)
    {
        builder.ToTable("procurement_market_cells");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.NaicsCode).HasColumnName("naics_code").HasMaxLength(6).IsRequired();
        builder.Property(c => c.State).HasColumnName("state").HasMaxLength(2).IsRequired();
        builder.Property(c => c.SetAside).HasColumnName("set_aside")
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.TrailingTwelveMonthObligated)
            .HasColumnName("trailing_twelve_month_obligated").HasColumnType("numeric(18,2)");
        builder.Property(c => c.AsOfDate).HasColumnName("as_of_date");

        // One cell per (NAICS, state, set-aside) combination.
        builder.HasIndex(c => new { c.NaicsCode, c.State, c.SetAside }).IsUnique();
    }
}
