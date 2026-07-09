using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("Stocks");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Symbol)
            .IsRequired()
            .HasMaxLength(20)
            .IsUnicode(false);

        builder.HasIndex(s => s.Symbol)
            .IsUnique();

        builder.Property(s => s.YahooSymbol)
            .IsRequired()
            .HasMaxLength(25)
            .IsUnicode(false);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Sector)
            .HasMaxLength(100);

        builder.Property(s => s.Industry)
            .HasMaxLength(100);

        builder.Property(s => s.Currency)
            .IsRequired()
            .HasMaxLength(10)
            .IsUnicode(false)
            .HasDefaultValue("TRY");

        builder.Property(s => s.Exchange)
            .IsRequired()
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("IST");

        builder.Property(s => s.IsActive)
            .HasDefaultValue(true);

        builder.Property(s => s.NeedsHistoryRefresh)
            .HasDefaultValue(false);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.HasMany(s => s.PriceHistories)
            .WithOne(p => p.Stock)
            .HasForeignKey(p => p.StockId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.CorporateActions)
            .WithOne(a => a.Stock)
            .HasForeignKey(a => a.StockId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
