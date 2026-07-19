using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Data.Configurations;

public class TopGainerConfiguration : IEntityTypeConfiguration<TopGainer>
{
    public void Configure(EntityTypeBuilder<TopGainer> builder)
    {
        builder.ToTable("TopGainers");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Period)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(t => t.Rank).IsRequired();

        builder.Property(t => t.Symbol)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.ReturnPct).HasPrecision(18, 4);
        builder.Property(t => t.StartPrice).HasPrecision(18, 6);
        builder.Property(t => t.EndPrice).HasPrecision(18, 6);

        builder.Property(t => t.StartDate).HasColumnType("date");
        builder.Property(t => t.EndDate).HasColumnType("date");
        builder.Property(t => t.ComputedAt).IsRequired();

        builder.HasIndex(t => new { t.Period, t.Rank })
            .IsUnique()
            .HasDatabaseName("IX_TopGainers_Period_Rank");

        builder.HasOne(t => t.Stock)
            .WithMany()
            .HasForeignKey(t => t.StockId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
