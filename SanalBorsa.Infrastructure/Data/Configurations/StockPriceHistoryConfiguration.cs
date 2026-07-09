using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class StockPriceHistoryConfiguration : IEntityTypeConfiguration<StockPriceHistory>
{
    public void Configure(EntityTypeBuilder<StockPriceHistory> builder)
    {
        builder.ToTable("StockPriceHistories");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedOnAdd();

        builder.Property(p => p.Date)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(p => p.Open)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(p => p.High)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(p => p.Low)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(p => p.Close)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(p => p.AdjustedClose)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(p => p.Volume)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // Composite unique index: one record per stock per day
        builder.HasIndex(p => new { p.StockId, p.Date })
            .IsUnique()
            .HasDatabaseName("IX_StockPriceHistories_StockId_Date");

        // Covering index for time-series range queries
        builder.HasIndex(p => new { p.StockId, p.Date, p.Close })
            .HasDatabaseName("IX_StockPriceHistories_StockId_Date_Close");
    }
}
