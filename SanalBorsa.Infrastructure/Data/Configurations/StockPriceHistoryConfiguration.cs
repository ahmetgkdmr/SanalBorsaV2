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

        // Diğer fiyat alanlarından farklı olarak daha yüksek hassasiyet gerekiyor: bu, gerçek bir
        // işlem fiyatı değil, split+temettü dahil kümülatif getiriyi yansıtan türetilmiş bir değer —
        // 30+ yıllık, çok sayıda bedelsiz/bedelli geçirmiş BIST hisselerinde (ör. GARAN 1991) kuruşun
        // çok altına inebiliyor (₺0,00012 gibi). 4 ondalık basamak bunu ₺0,0001'e yuvarlayıp Zaman
        // Makinesi hesabında %20+ hataya yol açıyordu (bkz. proje sohbeti — GARAN doğrulama testi).
        builder.Property(p => p.AdjustedClose)
            .IsRequired()
            .HasPrecision(24, 10);

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

        // Tarih öncelikli tarama (zaman makinesi liderlik tablosu her gece tüm geçmişi
        // tarih sırasıyla okur; bu indeks olmadan sıralama tüm tabloyu diske döküyor).
        builder.HasIndex(p => new { p.Date, p.StockId })
            .HasDatabaseName("IX_StockPriceHistories_Date_StockId")
            .IncludeProperties(p => p.Close);
    }
}
