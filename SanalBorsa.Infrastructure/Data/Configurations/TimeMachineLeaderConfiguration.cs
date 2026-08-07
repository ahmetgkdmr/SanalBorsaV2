using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class TimeMachineLeaderConfiguration : IEntityTypeConfiguration<TimeMachineLeader>
{
    public void Configure(EntityTypeBuilder<TimeMachineLeader> builder)
    {
        builder.ToTable("TimeMachineLeaders");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Category)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(t => t.StartDate).HasColumnType("date");
        builder.Property(t => t.EndDate).HasColumnType("date");

        builder.Property(t => t.Rank).IsRequired();
        builder.Property(t => t.StockId).IsRequired();

        builder.Property(t => t.Symbol)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.StartPrice).HasPrecision(18, 6);
        builder.Property(t => t.EndPrice).HasPrecision(18, 6);
        // AdjustedClose (decimal(24,10)) ile aynı hassasiyet — 4 ondalığa yuvarlama, büyük
        // çarpanlarda (40x+) tek-hisse simülasyonuyla TL bazında birkaç liralık farka yol açıyordu.
        builder.Property(t => t.ReturnPct).HasPrecision(24, 10);

        builder.Property(t => t.ComputedAt).IsRequired();

        // Sorgu deseni: kategori + "seçilen tarihe eşit/önceki en yakın gün".
        builder.HasIndex(t => new { t.Category, t.StartDate, t.Rank })
            .IsUnique()
            .HasDatabaseName("IX_TimeMachineLeaders_Category_StartDate_Rank");
    }
}
