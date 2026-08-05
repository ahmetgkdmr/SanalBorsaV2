using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class StockIntradayBarConfiguration : IEntityTypeConfiguration<StockIntradayBar>
{
    public void Configure(EntityTypeBuilder<StockIntradayBar> builder)
    {
        builder.ToTable("StockIntradayBars");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .ValueGeneratedOnAdd();

        builder.Property(b => b.BarTime)
            .IsRequired();

        builder.Property(b => b.Close)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.HasIndex(b => new { b.StockId, b.BarTime })
            .IsUnique()
            .HasDatabaseName("IX_StockIntradayBars_StockId_BarTime");

        // Bir hissenin tüm günün bar'larını sıralı çekmek için kapsayan index.
        builder.HasIndex(b => b.StockId)
            .HasDatabaseName("IX_StockIntradayBars_StockId")
            .IncludeProperties(b => new { b.BarTime, b.Close });

        builder.HasOne(b => b.Stock)
            .WithMany()
            .HasForeignKey(b => b.StockId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
